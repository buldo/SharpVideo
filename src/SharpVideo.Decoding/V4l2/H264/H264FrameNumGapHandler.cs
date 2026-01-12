namespace SharpVideo.Decoding.V4l2.H264;

using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

/// <summary>
/// Handles frame_num gap filling for H.264 decoding.
/// When gaps_in_frame_num_value_allowed_flag is set and a gap is detected,
/// non-existing pictures must be created to maintain proper reference picture management.
/// Following GStreamer's gst_h264_decoder_handle_frame_num_gap.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class H264FrameNumGapHandler
{
    private readonly ILogger? _logger;
    private uint _prevRefFrameNum;

    /// <summary>
    /// Gets the previous reference frame number.
    /// Used for FrameNumWrap calculation.
    /// </summary>
    public uint PrevRefFrameNum => _prevRefFrameNum;

    public H264FrameNumGapHandler(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Reset the handler state.
    /// </summary>
    public void Reset()
    {
        _prevRefFrameNum = 0;
    }

    /// <summary>
    /// Update the previous reference frame number (call after processing a reference picture).
    /// </summary>
    public void UpdatePrevRefFrameNum(uint frameNum)
    {
        _prevRefFrameNum = frameNum;
    }

    /// <summary>
    /// Check if there is a frame_num gap and handle it by creating non-existing pictures.
    /// Returns true if handled successfully, false on error.
    /// Following GStreamer's gst_h264_decoder_handle_frame_num_gap.
    /// </summary>
    /// <param name="dpb">The DPB to add non-existing pictures to</param>
    /// <param name="frameNum">The current frame_num from slice header</param>
    /// <param name="maxFrameNum">MaxFrameNum from SPS (1 &lt;&lt; (log2_max_frame_num_minus4 + 4))</param>
    /// <param name="gapsAllowed">gaps_in_frame_num_value_allowed_flag from SPS</param>
    /// <param name="isIdr">Whether the current picture is an IDR</param>
    /// <param name="createNonExistingPicture">Function to create a non-existing picture for a given frame_num</param>
    /// <returns>List of created non-existing pictures, or null on error</returns>
    public List<H264Picture>? HandleFrameNumGap(
        H264Dpb dpb,
        uint frameNum,
        int maxFrameNum,
        bool gapsAllowed,
        bool isIdr,
        Func<uint, H264Picture?> createNonExistingPicture)
    {
        // IDR resets prev_ref_frame_num
        if (isIdr)
        {
            _prevRefFrameNum = 0;
        }

        _logger?.LogTrace("HandleFrameNumGap: frame_num={FrameNum}, prev_ref_frame_num={PrevRefFrameNum}, maxFrameNum={MaxFrameNum}, isIdr={IsIdr}",
            frameNum, _prevRefFrameNum, maxFrameNum, isIdr);

        // Check if frame_num is expected
        if (_prevRefFrameNum == frameNum)
        {
            _logger?.LogTrace("frame_num == PrevRefFrameNum ({FrameNum}), not a gap", frameNum);
            return new List<H264Picture>();
        }

        if (((_prevRefFrameNum + 1) % (uint)maxFrameNum) == frameNum)
        {
            _logger?.LogTrace("frame_num == (PrevRefFrameNum + 1) % MaxFrameNum ({FrameNum}), not a gap", frameNum);
            return new List<H264Picture>();
        }

        // No pictures in DPB - no gap handling needed
        if (dpb.NumPics == 0)
        {
            _logger?.LogTrace("DPB is empty, not a gap");
            return new List<H264Picture>();
        }

        // Gap detected but not allowed
        if (!gapsAllowed)
        {
            // This is likely the case where some frames were dropped.
            // GStreamer continues decoding without error in this case.
            // Use Debug level to avoid excessive noise in live streams with packet loss.
            _logger?.LogDebug("Invalid frame_num {FrameNum} (prev_ref_frame_num {PrevRefFrameNum}), gaps not allowed - possible frame drop",
                frameNum, _prevRefFrameNum);
            return new List<H264Picture>();
        }

        _logger?.LogDebug("Handling frame_num gap {PrevFrameNum} -> {FrameNum} (MaxFrameNum: {MaxFrameNum})",
            _prevRefFrameNum, frameNum, maxFrameNum);

        // Fill in non-existing pictures for the gap (H.264 spec 7.4.3/7-23)
        var nonExistingPictures = new List<H264Picture>();
        uint unusedShortTermFrameNum = (_prevRefFrameNum + 1) % (uint)maxFrameNum;

        while (unusedShortTermFrameNum != frameNum)
        {
            var nonExistingPic = createNonExistingPicture(unusedShortTermFrameNum);
            if (nonExistingPic == null)
            {
                _logger?.LogError("Failed to create non-existing picture for frame_num {FrameNum}", unusedShortTermFrameNum);
                return null;
            }

            nonExistingPic.IsNonExisting = true;
            nonExistingPic.IsRef = true; // Short-term reference
            nonExistingPic.IsLongTermRef = false;
            nonExistingPic.FrameNum = unusedShortTermFrameNum;

            // Calculate FrameNumWrap and PicNum for the non-existing picture
            nonExistingPic.FrameNumWrap = (int)unusedShortTermFrameNum;
            nonExistingPic.PicNum = nonExistingPic.FieldPicFlag
                ? 2 * nonExistingPic.FrameNumWrap + (nonExistingPic.BottomFieldFlag ? 1 : 0)
                : nonExistingPic.FrameNumWrap;

            nonExistingPictures.Add(nonExistingPic);

            // Perform sliding window marking before adding to DPB
            dpb.PerformSlidingWindowMarking(dpb.MaxNumRefFrames);
            dpb.Add(nonExistingPic);

            _logger?.LogDebug("Created non-existing picture for frame_num {FrameNum}", unusedShortTermFrameNum);

            unusedShortTermFrameNum = (unusedShortTermFrameNum + 1) % (uint)maxFrameNum;
        }

        return nonExistingPictures;
    }
}
