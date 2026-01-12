namespace SharpVideo.Decoding.V4l2.H264;

using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.H264;

/// <summary>
/// Handles reference picture marking for H.264 decoding.
/// Implements both sliding window and adaptive reference picture marking (MMCO).
/// Following GStreamer's gst_h264_decoder_reference_picture_marking and
/// gst_h264_decoder_handle_memory_management_opt.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class H264ReferencePictureMarking
{
    private readonly ILogger? _logger;
    private int _maxLongTermFrameIdx = -1;

    public H264ReferencePictureMarking(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Reset the marking state.
    /// </summary>
    public void Reset()
    {
        _maxLongTermFrameIdx = -1;
    }

    /// <summary>
    /// Perform reference picture marking.
    /// Following GStreamer's gst_h264_decoder_reference_picture_marking.
    /// </summary>
    public bool PerformMarking(H264Dpb dpb, H264Picture picture, DecRefPicMarkingState? decRefPicMarking)
    {
        // If the current picture is an IDR, all reference pictures are unmarked
        if (picture.IsIdr)
        {
            dpb.MarkAllNonRef();

            if (decRefPicMarking?.long_term_reference_flag == 1)
            {
                picture.IsRef = true;
                picture.IsLongTermRef = true;
                picture.LongTermFrameIdx = 0;
                _maxLongTermFrameIdx = 0;
            }
            else
            {
                picture.IsRef = true;
                picture.IsLongTermRef = false;
                _maxLongTermFrameIdx = -1;
            }

            return true;
        }

        // Not an IDR. If the stream contains instructions on how to discard pictures
        // from DPB and how to mark/unmark existing reference pictures, do so.
        // Otherwise, fall back to default sliding window process
        if (decRefPicMarking?.adaptive_ref_pic_marking_mode_flag == 1)
        {
            if (picture.IsNonExisting)
            {
                _logger?.LogWarning(
                    "Invalid memory management operation for non-existing picture frame_num={FrameNum}",
                    picture.FrameNum);
            }

            return HandleAdaptiveRefPicMarking(dpb, picture, decRefPicMarking);
        }

        return dpb.PerformSlidingWindowMarking(dpb.MaxNumRefFrames) || true;
    }

    /// <summary>
    /// Handle adaptive reference picture marking (MMCO operations).
    /// Following GStreamer's gst_h264_decoder_handle_memory_management_opt.
    /// </summary>
    private bool HandleAdaptiveRefPicMarking(H264Dpb dpb, H264Picture picture, DecRefPicMarkingState decRefPicMarking)
    {
        for (int i = 0; i < decRefPicMarking.memory_management_control_operation.Count; i++)
        {
            uint type = decRefPicMarking.memory_management_control_operation[i];

            _logger?.LogTrace("MMCO type {Type}", type);

            // Normal end of operations' specification
            if (type == 0)
            {
                return true;
            }

            switch (type)
            {
                case 1:
                    // Mark a short-term reference picture as "unused for reference"
                    if (!HandleMmco1(dpb, picture, decRefPicMarking, i))
                    {
                        _logger?.LogWarning("MMCO 1 failed");
                    }
                    break;

                case 2:
                    // Mark a long-term reference picture as "unused for reference"
                    if (!HandleMmco2(dpb, decRefPicMarking, i))
                    {
                        _logger?.LogWarning("MMCO 2 failed");
                    }
                    break;

                case 3:
                    // Mark a short-term reference picture as "used for long-term reference"
                    if (!HandleMmco3(dpb, picture, decRefPicMarking, i))
                    {
                        _logger?.LogWarning("MMCO 3 failed");
                    }
                    break;

                case 4:
                    // Specify the maximum long-term frame index
                    if (i < decRefPicMarking.max_long_term_frame_idx_plus1.Count)
                    {
                        _maxLongTermFrameIdx = (int)decRefPicMarking.max_long_term_frame_idx_plus1[i] - 1;
                        // Mark all long-term refs with index > max as unused
                        MarkLongTermRefsAboveMax(dpb);
                    }
                    break;

                case 5:
                    // Mark all reference pictures as "unused for reference"
                    dpb.MarkAllNonRef();
                    _maxLongTermFrameIdx = -1;
                    // Memory management control operation 5 is special
                    // (sets POC to 0, clears DPB)
                    picture.TopFieldOrderCnt = 0;
                    picture.BottomFieldOrderCnt = 0;
                    picture.MemMgmt5 = true;
                    break;

                case 6:
                    // Mark the current picture as "used for long-term reference"
                    if (!HandleMmco6(picture, decRefPicMarking, i))
                    {
                        _logger?.LogWarning("MMCO 6 failed");
                    }
                    break;

                default:
                    _logger?.LogWarning("Unknown MMCO type {Type}", type);
                    break;
            }
        }

        return true;
    }

    private bool HandleMmco1(H264Dpb dpb, H264Picture currentPicture, DecRefPicMarkingState marking, int index)
    {
        if (index >= marking.difference_of_pic_nums_minus1.Count)
        {
            return false;
        }

        int diffPicNum = (int)marking.difference_of_pic_nums_minus1[index] + 1;
        int picNumX = currentPicture.PicNum - diffPicNum;

        var pic = dpb.FindShortTermRef(picNumX);
        if (pic == null)
        {
            _logger?.LogWarning("MMCO 1: Short-term ref with pic_num {PicNum} not found", picNumX);
            return false;
        }

        pic.IsRef = false;
        _logger?.LogTrace("MMCO 1: Marked short-term ref pic_num={PicNum} as unused", picNumX);
        return true;
    }

    private bool HandleMmco2(H264Dpb dpb, DecRefPicMarkingState marking, int index)
    {
        if (index >= marking.long_term_pic_num.Count)
        {
            return false;
        }

        int longTermPicNum = (int)marking.long_term_pic_num[index];
        var pic = dpb.FindLongTermRef(longTermPicNum);
        if (pic == null)
        {
            _logger?.LogWarning("MMCO 2: Long-term ref with long_term_pic_num {LongTermPicNum} not found", longTermPicNum);
            return false;
        }

        pic.IsRef = false;
        pic.IsLongTermRef = false;
        _logger?.LogTrace("MMCO 2: Marked long-term ref long_term_pic_num={LongTermPicNum} as unused", longTermPicNum);
        return true;
    }

    private bool HandleMmco3(H264Dpb dpb, H264Picture currentPicture, DecRefPicMarkingState marking, int index)
    {
        if (index >= marking.difference_of_pic_nums_minus1.Count ||
            index >= marking.long_term_frame_idx.Count)
        {
            return false;
        }

        int diffPicNum = (int)marking.difference_of_pic_nums_minus1[index] + 1;
        int picNumX = currentPicture.PicNum - diffPicNum;
        int longTermFrameIdx = (int)marking.long_term_frame_idx[index];

        // First, unmark any existing picture with this long_term_frame_idx
        foreach (var existingPic in dpb.GetPictures())
        {
            if (existingPic.IsLongTermRef && existingPic.LongTermFrameIdx == longTermFrameIdx)
            {
                existingPic.IsRef = false;
                existingPic.IsLongTermRef = false;
            }
        }

        var pic = dpb.FindShortTermRef(picNumX);
        if (pic == null)
        {
            _logger?.LogWarning("MMCO 3: Short-term ref with pic_num {PicNum} not found", picNumX);
            return false;
        }

        pic.IsLongTermRef = true;
        pic.LongTermFrameIdx = longTermFrameIdx;
        _logger?.LogTrace("MMCO 3: Converted short-term ref pic_num={PicNum} to long-term idx={Idx}",
            picNumX, longTermFrameIdx);
        return true;
    }

    private bool HandleMmco6(H264Picture picture, DecRefPicMarkingState marking, int index)
    {
        if (index >= marking.long_term_frame_idx.Count)
        {
            return false;
        }

        int longTermFrameIdx = (int)marking.long_term_frame_idx[index];
        picture.IsLongTermRef = true;
        picture.LongTermFrameIdx = longTermFrameIdx;
        _logger?.LogTrace("MMCO 6: Marked current picture as long-term idx={Idx}", longTermFrameIdx);
        return true;
    }

    private void MarkLongTermRefsAboveMax(H264Dpb dpb)
    {
        foreach (var pic in dpb.GetPictures())
        {
            if (pic.IsLongTermRef && pic.LongTermFrameIdx > _maxLongTermFrameIdx)
            {
                pic.IsRef = false;
                pic.IsLongTermRef = false;
            }
        }
    }
}
