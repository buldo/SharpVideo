using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.H264;
using SharpVideo.Linux.Native.V4L2;

namespace SharpVideo.Decoding.V4l2.H264;

/// <summary>
/// DPB bumping mode, controlling output latency.
/// Following GStreamer's GstH264DpbBumpMode.
/// </summary>
public enum H264DpbBumpMode
{
    /// <summary>Normal latency - wait for DPB to fill before bumping.</summary>
    NormalLatency,
    /// <summary>Low latency - bump earlier for live streams.</summary>
    LowLatency,
    /// <summary>Very low latency - bump as soon as possible.</summary>
    VeryLowLatency
}

/// <summary>
/// Decoded Picture Buffer (DPB) for H.264 decoding.
/// Manages reference pictures according to H.264 specification.
/// Mirrors the DPB management in GStreamer's GstH264Dpb.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class H264Dpb
{
    private readonly List<H264Picture> _pictures = new();
    private readonly ILogger? _logger;

    /// <summary>
    /// Maximum number of pictures in the DPB.
    /// </summary>
    public int MaxNumPics { get; private set; }

    /// <summary>
    /// Maximum number of reference frames (from SPS max_num_ref_frames).
    /// </summary>
    public int MaxNumRefFrames { get; private set; }

    /// <summary>
    /// Maximum number of reorder frames for output ordering.
    /// </summary>
    public int MaxNumReorderFrames { get; private set; }

    /// <summary>
    /// Whether the stream is interlaced.
    /// </summary>
    public bool Interlaced { get; set; }

    /// <summary>
    /// Last outputted POC for tracking output order.
    /// </summary>
    public int LastOutputPoc { get; private set; } = int.MinValue;

    /// <summary>
    /// Whether the last outputted picture was a non-reference picture.
    /// Following GStreamer's last_output_non_ref for low-latency bumping.
    /// </summary>
    public bool LastOutputNonRef { get; private set; }

    /// <summary>
    /// Number of pictures currently in the DPB.
    /// </summary>
    public int NumPics => _pictures.Count;

    /// <summary>
    /// Number of reference pictures currently in the DPB.
    /// </summary>
    public int NumRefPics => _pictures.Count(p => p.IsRef);

    /// <summary>
    /// Number of pictures needed for output (not yet bumped).
    /// Following GStreamer's num_output_needed counter.
    /// </summary>
    public int NumOutputNeeded { get; private set; }

    /// <summary>
    /// Whether the DPB is full.
    /// </summary>
    public bool IsFull => NumRefPics >= MaxNumRefFrames;

    public H264Dpb(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Set the maximum number of pictures in the DPB based on SPS.
    /// </summary>
    public void SetMaxNumPics(int maxNumPics)
    {
        MaxNumPics = maxNumPics;
    }

    /// <summary>
    /// Set the maximum number of reference frames from SPS.
    /// </summary>
    public void SetMaxNumRefFrames(int maxNumRefFrames)
    {
        MaxNumRefFrames = Math.Max(1, maxNumRefFrames);
    }

    /// <summary>
    /// Set the maximum number of reorder frames for output ordering.
    /// </summary>
    public void SetMaxNumReorderFrames(int maxNumReorderFrames)
    {
        MaxNumReorderFrames = maxNumReorderFrames;
    }

    /// <summary>
    /// Clear all pictures from the DPB.
    /// </summary>
    public void Clear()
    {
        foreach (var pic in _pictures)
        {
            pic.Dispose();
        }
        _pictures.Clear();
        LastOutputPoc = int.MinValue;
        LastOutputNonRef = false;
        NumOutputNeeded = 0;
    }

    /// <summary>
    /// Add a picture to the DPB.
    /// Following GStreamer's gst_h264_dpb_add.
    /// </summary>
    public void Add(H264Picture picture)
    {
        // C.4.2 Decoding of gaps in frame_num and storage of "non-existing" pictures
        // The "non-existing" frame is stored in an empty frame buffer and is marked
        // as "not needed for output", and the DPB fullness is incremented by one
        if (!picture.IsNonExisting)
        {
            picture.NeededForOutput = true;

            if (picture.Field == H264PictureField.Frame)
            {
                NumOutputNeeded++;
            }
            else
            {
                // We can do output only when field pair are complete
                if (picture.SecondField)
                {
                    NumOutputNeeded++;
                }
            }
        }
        else
        {
            picture.NeededForOutput = false;
        }

        // Link each field
        if (picture.SecondField && picture.OtherField != null)
        {
            picture.OtherField.OtherField = picture;
        }

        _pictures.Add(picture);

        if (_pictures.Count > MaxNumPics * (Interlaced ? 2 : 1))
        {
            _logger?.LogError("DPB size is {Size}, exceeds max size {Max}",
                _pictures.Count, MaxNumPics * (Interlaced ? 2 : 1));
        }

        // The IDR frame or mem_mgmt_5 resets last_output_poc
        if (picture.GetPicOrderCnt() == 0)
        {
            _logger?.LogTrace("last_output_poc reset because of IDR or mem_mgmt_5");
            LastOutputPoc = int.MinValue;
            LastOutputNonRef = false;
        }
    }

    /// <summary>
    /// Remove a picture from the DPB.
    /// </summary>
    public void Remove(H264Picture picture)
    {
        _pictures.Remove(picture);
        picture.Dispose();
    }

    /// <summary>
    /// Get all pictures in the DPB.
    /// </summary>
    public IReadOnlyList<H264Picture> GetPictures() => _pictures;

    /// <summary>
    /// Find a short-term reference picture by pic_num.
    /// </summary>
    public H264Picture? FindShortTermRef(int picNum)
    {
        return _pictures.FirstOrDefault(p => p.IsRef && !p.IsLongTermRef && p.PicNum == picNum);
    }

    /// <summary>
    /// Find a long-term reference picture by long_term_pic_num.
    /// </summary>
    public H264Picture? FindLongTermRef(int longTermPicNum)
    {
        return _pictures.FirstOrDefault(p => p.IsRef && p.IsLongTermRef && p.LongTermPicNum == longTermPicNum);
    }

    /// <summary>
    /// Find a picture by frame_num.
    /// </summary>
    public H264Picture? FindByFrameNum(uint frameNum)
    {
        return _pictures.FirstOrDefault(p => p.FrameNum == frameNum);
    }

    /// <summary>
    /// Mark all reference pictures as unused for reference.
    /// Used for IDR picture processing.
    /// </summary>
    public void MarkAllNonRef()
    {
        foreach (var pic in _pictures)
        {
            pic.IsRef = false;
            pic.IsLongTermRef = false;
        }
    }

    /// <summary>
    /// Perform sliding window marking process.
    /// Removes the oldest short-term reference when DPB is full.
    /// Following H.264 spec 8.2.5.3 and GStreamer's gst_h264_dpb_perform_sliding_window.
    /// </summary>
    /// <returns>True if a picture was marked as non-reference</returns>
    public bool PerformSlidingWindowMarking(int maxNumRefFrames)
    {
        // Count short-term references only for sliding window
        var numShortTermRefs = _pictures.Count(p => p.IsRef && !p.IsLongTermRef);
        var numLongTermRefs = _pictures.Count(p => p.IsRef && p.IsLongTermRef);

        // Sliding window applies when short-term + long-term refs >= max_num_ref_frames
        if (numShortTermRefs + numLongTermRefs < maxNumRefFrames)
        {
            return false;
        }

        // Find the oldest short-term reference (smallest FrameNumWrap per H.264 spec 8.2.5.3)
        var shortTermRefs = _pictures
            .Where(p => p.IsRef && !p.IsLongTermRef)
            .OrderBy(p => p.FrameNumWrap)
            .ToList();

        if (shortTermRefs.Count > 0)
        {
            var oldest = shortTermRefs[0];
            oldest.IsRef = false;
            _logger?.LogTrace("Sliding window: marked picture frame_num={FrameNum} (FrameNumWrap={FrameNumWrap}) as non-ref",
                oldest.FrameNum, oldest.FrameNumWrap);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Remove pictures that are no longer reference and not needed for output.
    /// Returns the removed pictures so caller can handle buffer lifecycle.
    /// Following GStreamer's gst_h264_dpb_delete_unused.
    /// Note: Uses NeededForOutput (not Outputted) - a picture is unused when:
    /// 1. Not used as reference (IsRef == false)
    /// 2. Not needed for output (NeededForOutput == false)
    /// </summary>
    public List<H264Picture> RemoveUnusedPictures()
    {
        // NOTE: don't use fast removal - the last picture needs to be referenced for bumping decision
        var toRemove = new List<H264Picture>();
        for (int i = 0; i < _pictures.Count; i++)
        {
            var pic = _pictures[i];
            if (!pic.NeededForOutput && !pic.IsRef)
            {
                _logger?.LogTrace("Removing unused picture from DPB: frame_num={FrameNum}, field={Field}",
                    pic.FrameNum, pic.Field);
                toRemove.Add(pic);
                _pictures.RemoveAt(i);
                i--;
            }
        }
        return toRemove;
    }

    /// <summary>
    /// Remove all pictures that are not reference and have been outputted.
    /// Deprecated: use RemoveUnusedPictures instead.
    /// </summary>
    public List<H264Picture> DrainOutputtedNonRef()
    {
        var toRemove = _pictures.Where(p => !p.IsRef && !p.NeededForOutput).ToList();
        foreach (var pic in toRemove)
        {
            _pictures.Remove(pic);
        }
        return toRemove;
    }

    /// <summary>
    /// Get pictures needing output (POC order, not yet outputted).
    /// </summary>
    public List<H264Picture> GetPicsForOutput()
    {
        return _pictures
            .Where(p => p.NeededForOutput && !p.IsNonExisting)
            .OrderBy(p => p.GetPicOrderCnt())
            .ToList();
    }

    // ============================================
    // DPB Bumping (C.4.5)
    // Following GStreamer's gst_h264_dpb_needs_bump and gst_h264_dpb_bump
    // ============================================

    /// <summary>
    /// Get the picture with lowest POC that needs output, along with its index.
    /// Returns -1 if no such picture exists.
    /// Following GStreamer's gst_h264_dpb_get_lowest_output_needed_picture.
    /// </summary>
    private int GetLowestOutputNeededPicture(bool force, out H264Picture? picture)
    {
        picture = null;
        int index = -1;
        H264Picture? lowest = null;

        for (int i = 0; i < _pictures.Count; i++)
        {
            var pic = _pictures[i];

            if (!force && !pic.NeededForOutput)
                continue;

            // Skip second field or incomplete field pair
            if (pic.Field != H264PictureField.Frame &&
                (pic.OtherField == null || pic.SecondField))
                continue;

            if (lowest == null || pic.GetPicOrderCnt() < lowest.GetPicOrderCnt())
            {
                lowest = pic;
                index = i;
            }
        }

        picture = lowest;
        return index;
    }

    /// <summary>
    /// Check if the DPB needs to bump (output) a picture.
    /// Following GStreamer's gst_h264_dpb_needs_bump.
    /// </summary>
    /// <param name="currentPicture">The current picture being decoded (may be null for drain).</param>
    /// <param name="bumpMode">The bumping mode to use.</param>
    /// <returns>True if a picture needs to be bumped.</returns>
    public bool NeedsBump(H264Picture? currentPicture, H264DpbBumpMode bumpMode)
    {
        if (NumOutputNeeded < 0)
        {
            _logger?.LogWarning("NumOutputNeeded is negative: {Count}", NumOutputNeeded);
        }

        int lowestIndex = GetLowestOutputNeededPicture(false, out var lowestPic);
        if (lowestIndex < 0)
        {
            // No picture needed for output, check normal bumping
            goto normal_bump;
        }

        int lowestPoc = lowestPic!.GetPicOrderCnt();
        bool isRefPicture = lowestPic.IsRef;

        if (bumpMode >= H264DpbBumpMode.LowLatency)
        {
            // Low latency mode - try to bump as soon as possible without frame disorder
            // Do not support interlaced mode for low latency
            if (Interlaced)
            {
                goto normal_bump;
            }

            // Equal to normal bump if DPB is full
            if (!HasEmptyFrameBuffer())
            {
                goto normal_bump;
            }

            // For POC type 2, decoding order is equal to output order
            if (lowestPic.PicOrderCntType == 2)
            {
                _logger?.LogTrace("POC type == 2, bumping");
                return true;
            }

            // Continuous non-reference frames can be bumped safely
            if (LastOutputNonRef && !isRefPicture)
            {
                _logger?.LogTrace("Continuous non-reference frame poc: {LastPoc} -> {CurrentPoc}, bumping for low-latency",
                    LastOutputPoc, lowestPoc);
                return true;
            }

            // num_reorder_frames check
            if (lowestIndex >= MaxNumReorderFrames)
            {
                int needOutput = 0;
                for (int i = 0; i < lowestIndex; i++)
                {
                    if (_pictures[i].NeededForOutput)
                        needOutput++;
                }

                if (needOutput >= MaxNumReorderFrames)
                {
                    _logger?.LogTrace("Frame with lowest poc {Poc} has {Count} preceding frames, satisfies num_reorder_frames {Max}, bumping",
                        lowestPoc, lowestIndex, MaxNumReorderFrames);
                    return true;
                }
            }

            // Bump leading pictures with negative POC if already found positive POC
            if (currentPicture != null && currentPicture.GetPicOrderCnt() > 0 && lowestPoc < 0)
            {
                _logger?.LogTrace("Negative poc {Poc}, bumping for low-latency", lowestPoc);
                return true;
            }

            // IDR or mem_mgmt_5 frame
            if (lowestPoc == 0 && _pictures.Count <= 1)
            {
                if (currentPicture != null && currentPicture.GetPicOrderCnt() > lowestPoc)
                {
                    _logger?.LogTrace("IDR or mem_mgmt_5 frame, bumping for low-latency");
                    return true;
                }
                goto normal_bump;
            }

            // Non-ref frame with lowest POC can be safely bumped
            if (!isRefPicture)
            {
                _logger?.LogTrace("Non-ref with lowest-poc: {Poc}, bumping for low-latency", lowestPoc);
                return true;
            }

            // When inserting non-ref frame with bigger POC
            if (currentPicture != null && !currentPicture.IsRef && lowestPoc < currentPicture.GetPicOrderCnt())
            {
                _logger?.LogTrace("lowest-poc: {LowestPoc} < to insert non ref pic: {CurrentPoc}, bumping for low-latency",
                    lowestPoc, currentPicture.GetPicOrderCnt());
                return true;
            }

            if (bumpMode >= H264DpbBumpMode.VeryLowLatency)
            {
                // POC increment by <=2 - may cause disorder for some streams
                if (lowestPoc > LastOutputPoc && lowestPoc - LastOutputPoc <= 2)
                {
                    _logger?.LogTrace("lowest-poc: {LowestPoc}, last-output-poc: {LastPoc}, diff <= 2, bumping for very-low-latency",
                        lowestPoc, LastOutputPoc);
                    return true;
                }
            }
        }

    normal_bump:
        // C.4.5.3: The "bumping" process is invoked in the following cases:
        // - There is no empty frame buffer and a empty frame buffer is needed
        // - There is no empty frame buffer and current picture is a non-reference picture
        //   that precedes pictures in the DPB in output order
        if (HasEmptyFrameBuffer())
        {
            _logger?.LogTrace("DPB has empty frame buffer, no need bumping");
            return false;
        }

        if (currentPicture != null && currentPicture.IsRef)
        {
            _logger?.LogTrace("No empty frame buffer for ref frame, need bumping");
            return true;
        }

        // If we didn't get lowestPic earlier, try to get it now
        if (lowestPic == null)
        {
            GetLowestOutputNeededPicture(false, out lowestPic);
        }

        if (currentPicture != null && lowestPic != null)
        {
            int picPoc = lowestPic.GetPicOrderCnt();
            if (currentPicture.GetPicOrderCnt() > picPoc)
            {
                _logger?.LogTrace("No empty frame buffer, lowest poc {LowestPoc} < current poc {CurrentPoc}, need bumping",
                    picPoc, currentPicture.GetPicOrderCnt());
                return true;
            }
            else
            {
                _logger?.LogTrace("No empty frame buffer, but lowest poc {LowestPoc} >= current poc {CurrentPoc}, no need bumping",
                    picPoc, currentPicture.GetPicOrderCnt());
            }
        }

        return false;
    }

    /// <summary>
    /// Bump (output) the picture with the lowest POC from the DPB.
    /// Following GStreamer's gst_h264_dpb_bump.
    /// </summary>
    /// <param name="drain">Whether we are draining the DPB.</param>
    /// <returns>The picture to output, or null if none.</returns>
    public H264Picture? Bump(bool drain)
    {
        bool outputNeeded = true;
        int index = GetLowestOutputNeededPicture(false, out var picture);

        // Bumping is needed but has no output needed pictures. Pick the smallest POC picture
        if (picture == null && !drain)
        {
            index = GetLowestOutputNeededPicture(true, out picture);
            if (picture != null)
            {
                outputNeeded = false;
            }
        }

        if (picture == null || index < 0)
        {
            return null;
        }

        picture.NeededForOutput = false;

        if (outputNeeded)
        {
            NumOutputNeeded--;
        }

        if (NumOutputNeeded < 0)
        {
            _logger?.LogWarning("NumOutputNeeded went negative after bump");
            NumOutputNeeded = 0;
        }

        // NOTE: don't use fast removal - the last picture needs to be referenced for bumping decision
        // Remove from DPB if not a reference OR if draining OR if emergency bumping
        if (!picture.IsRef || drain || !outputNeeded)
        {
            _pictures.RemoveAt(index);
        }

        // Handle field pairs
        var otherPicture = picture.OtherField;
        if (otherPicture != null)
        {
            otherPicture.NeededForOutput = false;

            // At this moment, this picture should be interlaced
            // FIXME: need to check picture timing SEI for TFF decision
            // For now, use POC comparison

            if (!otherPicture.IsRef)
            {
                for (int i = 0; i < _pictures.Count; i++)
                {
                    if (_pictures[i] == otherPicture)
                    {
                        _pictures.RemoveAt(i);
                        break;
                    }
                }
            }
            // Now other field may or may not exist in DPB
        }

        LastOutputPoc = picture.GetPicOrderCnt();
        LastOutputNonRef = !picture.IsRef;
        picture.Outputted = true;

        return picture;
    }

    /// <summary>
    /// Count the number of frames (or complementary field pairs) in the DPB.
    /// Following GStreamer's frame counting logic.
    /// </summary>
    private int CountFramesInDpb()
    {
        if (!Interlaced)
        {
            return _pictures.Count;
        }

        // For interlaced, count frame pictures and field pairs as single frames
        int count = 0;
        var counted = new HashSet<H264Picture>();

        foreach (var pic in _pictures)
        {
            if (counted.Contains(pic))
            {
                continue;
            }

            count++;
            counted.Add(pic);

            if (pic.OtherField != null && _pictures.Contains(pic.OtherField))
            {
                counted.Add(pic.OtherField);
            }
        }

        return count;
    }

    /// <summary>
    /// Check if there is an empty frame buffer slot in the DPB.
    /// Following GStreamer's gst_h264_dpb_has_empty_frame_buffer.
    /// </summary>
    public bool HasEmptyFrameBuffer()
    {
        return CountFramesInDpb() < MaxNumPics;
    }

    /// <summary>
    /// Fill the V4L2 DPB array from the current DPB state.
    /// This mirrors GStreamer's gst_v4l2_codec_h264_dec_fill_decoder_params.
    /// </summary>
    public V4L2H264DpbEntry[] CreateV4L2Dpb()
    {
        var dpb = new V4L2H264DpbEntry[V4L2H264Constants.V4L2_H264_NUM_DPB_ENTRIES];

        // Initialize all entries
        for (int i = 0; i < dpb.Length; i++)
        {
            dpb[i] = new V4L2H264DpbEntry { Reserved = new byte[5] };
        }

        int entryId = 0;
        foreach (var refPic in _pictures)
        {
            // Skip non-reference pictures - they are not useful for decoding
            if (!refPic.IsRef)
            {
                continue;
            }

            // Skip second field pictures - they are handled together with first field
            if (refPic.SecondField)
            {
                continue;
            }

            if (entryId >= V4L2H264Constants.V4L2_H264_NUM_DPB_ENTRIES)
            {
                break;
            }

            // V4L2 uAPI uses pic_num for both PicNum and LongTermPicNum,
            // and frame_num for both FrameNum and LongTermFrameIdx
            int picNum = refPic.PicNum;
            uint frameNum = refPic.FrameNum;

            if (refPic.IsLongTermRef)
            {
                picNum = refPic.LongTermPicNum;
                frameNum = (uint)refPic.LongTermFrameIdx;
            }

            ref var entry = ref dpb[entryId];
            entry.ReferenceTimestamp = refPic.ReferenceTs;
            entry.FrameNum = (ushort)Math.Min(frameNum, ushort.MaxValue);
            entry.PicNum = (uint)picNum;
            entry.Flags = V4L2H264Constants.V4L2_H264_DPB_ENTRY_FLAG_VALID;

            if (refPic.IsRef)
            {
                entry.Flags |= V4L2H264Constants.V4L2_H264_DPB_ENTRY_FLAG_ACTIVE;
            }

            if (refPic.IsLongTermRef)
            {
                entry.Flags |= V4L2H264Constants.V4L2_H264_DPB_ENTRY_FLAG_LONG_TERM;
            }

            if (refPic.FieldPicFlag)
            {
                entry.Flags |= V4L2H264Constants.V4L2_H264_DPB_ENTRY_FLAG_FIELD;
            }

            // Fill field order counts based on picture field type
            switch (refPic.Field)
            {
                case H264PictureField.Frame:
                    entry.TopFieldOrderCnt = refPic.TopFieldOrderCnt;
                    entry.BottomFieldOrderCnt = refPic.BottomFieldOrderCnt;
                    entry.Fields = V4L2H264Constants.V4L2_H264_FRAME_REF;
                    break;

                case H264PictureField.TopField:
                    entry.TopFieldOrderCnt = refPic.TopFieldOrderCnt;
                    entry.Fields = V4L2H264Constants.V4L2_H264_TOP_FIELD_REF;
                    if (refPic.OtherField != null)
                    {
                        entry.BottomFieldOrderCnt = refPic.OtherField.BottomFieldOrderCnt;
                        entry.Fields |= V4L2H264Constants.V4L2_H264_BOTTOM_FIELD_REF;
                    }
                    break;

                case H264PictureField.BottomField:
                    entry.BottomFieldOrderCnt = refPic.BottomFieldOrderCnt;
                    entry.Fields = V4L2H264Constants.V4L2_H264_BOTTOM_FIELD_REF;
                    if (refPic.OtherField != null)
                    {
                        entry.TopFieldOrderCnt = refPic.OtherField.TopFieldOrderCnt;
                        entry.Fields |= V4L2H264Constants.V4L2_H264_TOP_FIELD_REF;
                    }
                    break;
            }

            entryId++;
        }

        return dpb;
    }

    /// <summary>
    /// Look up DPB index for a reference picture by its reference timestamp.
    /// Returns 0xff if not found.
    /// </summary>
    public static byte LookupDpbIndex(V4L2H264DpbEntry[] dpb, H264Picture? refPic)
    {
        if (refPic == null)
        {
            return 0xff;
        }

        // DPB entries store first field in a merged fashion
        var lookupPic = refPic.SecondField && refPic.OtherField != null
            ? refPic.OtherField
            : refPic;

        ulong refTs = lookupPic.ReferenceTs;

        for (int i = 0; i < dpb.Length; i++)
        {
            if ((dpb[i].Flags & V4L2H264Constants.V4L2_H264_DPB_ENTRY_FLAG_ACTIVE) != 0 &&
                dpb[i].ReferenceTimestamp == refTs)
            {
                return (byte)i;
            }
        }

        return 0xff;
    }
}
