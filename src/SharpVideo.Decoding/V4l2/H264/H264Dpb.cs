using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.H264;
using SharpVideo.Linux.Native.V4L2;

namespace SharpVideo.Decoding.V4l2.H264;

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
    /// Number of pictures currently in the DPB.
    /// </summary>
    public int NumPics => _pictures.Count;

    /// <summary>
    /// Number of reference pictures currently in the DPB.
    /// </summary>
    public int NumRefPics => _pictures.Count(p => p.IsRef);

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
    /// Clear all pictures from the DPB.
    /// </summary>
    public void Clear()
    {
        foreach (var pic in _pictures)
        {
            pic.Dispose();
        }
        _pictures.Clear();
    }

    /// <summary>
    /// Add a picture to the DPB.
    /// </summary>
    public void Add(H264Picture picture)
    {
        _pictures.Add(picture);
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
    /// </summary>
    public void PerformSlidingWindowMarking(int maxNumRefFrames)
    {
        if (NumRefPics < maxNumRefFrames)
        {
            return;
        }

        // Find the oldest short-term reference (smallest FrameNumWrap)
        var shortTermRefs = _pictures
            .Where(p => p.IsRef && !p.IsLongTermRef)
            .OrderBy(p => p.FrameNum)
            .ToList();

        if (shortTermRefs.Count > 0)
        {
            var oldest = shortTermRefs[0];
            oldest.IsRef = false;
            _logger?.LogTrace("Sliding window: marked picture frame_num={FrameNum} as non-ref", oldest.FrameNum);
        }
    }

    /// <summary>
    /// Remove pictures that are no longer reference and have been outputted.
    /// Returns the removed pictures so caller can handle buffer lifecycle.
    /// This mirrors GStreamer's gst_h264_dpb_delete_unused.
    /// </summary>
    public List<H264Picture> RemoveUnusedPictures()
    {
        // Find pictures that are not reference (no longer needed for decoding)
        // In a full implementation, we'd also check if they've been outputted
        var toRemove = _pictures.Where(p => !p.IsRef).ToList();
        foreach (var pic in toRemove)
        {
            _pictures.Remove(pic);
            _logger?.LogTrace("Removed unused picture from DPB: frame_num={FrameNum}", pic.FrameNum);
        }
        return toRemove;
    }

    /// <summary>
    /// Remove all pictures that are not reference and have been outputted.
    /// </summary>
    public List<H264Picture> DrainOutputtedNonRef()
    {
        var toRemove = _pictures.Where(p => !p.IsRef && p.Outputted).ToList();
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
            .Where(p => !p.Outputted && !p.IsNonExisting)
            .OrderBy(p => p.GetPicOrderCnt())
            .ToList();
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
