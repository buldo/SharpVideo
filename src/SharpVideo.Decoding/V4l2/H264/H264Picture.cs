using System.Runtime.Versioning;
using SharpVideo.H264;
using SharpVideo.Linux.Native.V4L2;
using SharpVideo.Utils;

namespace SharpVideo.Decoding.V4l2.H264;

/// <summary>
/// Represents an H.264 picture field type, matching GStreamer's GstH264PictureField.
/// </summary>
public enum H264PictureField
{
    /// <summary>Frame (non-interlaced) picture.</summary>
    Frame = 0,
    /// <summary>Top field picture.</summary>
    TopField = 1,
    /// <summary>Bottom field picture.</summary>
    BottomField = 2
}

/// <summary>
/// Represents an H.264 picture in the decoding process.
/// This mirrors GStreamer's GstH264Picture structure.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class H264Picture : IDisposable
{
    /// <summary>
    /// System frame number - a unique monotonically increasing number assigned to each picture.
    /// Used to generate reference timestamps.
    /// </summary>
    public uint SystemFrameNumber { get; init; }

    /// <summary>
    /// The frame_num from the slice header.
    /// </summary>
    public uint FrameNum { get; set; }

    /// <summary>
    /// Picture number for short-term reference pictures.
    /// </summary>
    public int PicNum { get; set; }

    /// <summary>
    /// Long-term picture number for long-term reference pictures.
    /// </summary>
    public int LongTermPicNum { get; set; }

    /// <summary>
    /// Long-term frame index.
    /// </summary>
    public int LongTermFrameIdx { get; set; }

    /// <summary>
    /// NAL reference IDC value.
    /// </summary>
    public byte NalRefIdc { get; set; }

    /// <summary>
    /// Whether this is an IDR picture.
    /// </summary>
    public bool IsIdr { get; set; }

    /// <summary>
    /// Whether this is a reference picture.
    /// </summary>
    public bool IsRef { get; set; }

    /// <summary>
    /// Whether this is a long-term reference picture.
    /// </summary>
    public bool IsLongTermRef { get; set; }

    /// <summary>
    /// Whether this is a non-existing picture (used for gaps in frame_num).
    /// </summary>
    public bool IsNonExisting { get; set; }

    /// <summary>
    /// Whether this is a field picture.
    /// </summary>
    public bool FieldPicFlag { get; set; }

    /// <summary>
    /// Whether this is the bottom field (only valid when FieldPicFlag is true).
    /// </summary>
    public bool BottomFieldFlag { get; set; }

    /// <summary>
    /// Whether this is the second field of a complementary field pair.
    /// </summary>
    public bool SecondField { get; set; }

    /// <summary>
    /// Top field order count.
    /// </summary>
    public int TopFieldOrderCnt { get; set; }

    /// <summary>
    /// Bottom field order count.
    /// </summary>
    public int BottomFieldOrderCnt { get; set; }

    /// <summary>
    /// The picture field type (frame, top, or bottom).
    /// </summary>
    public H264PictureField Field { get; set; } = H264PictureField.Frame;

    /// <summary>
    /// Reference to the other field for interlaced content.
    /// </summary>
    public H264Picture? OtherField { get; set; }

    /// <summary>
    /// The decoded frame buffer.
    /// </summary>
    public SharedDmaBuffer? Buffer { get; set; }

    /// <summary>
    /// Whether this picture has been outputted.
    /// </summary>
    public bool Outputted { get; set; }

    /// <summary>
    /// Get the reference timestamp in nanoseconds for V4L2 DPB.
    /// Following GStreamer convention: system_frame_number * 1000.
    /// </summary>
    public ulong ReferenceTs => (ulong)SystemFrameNumber * 1000;

    /// <summary>
    /// Get V4L2 DPB fields flag based on picture field type.
    /// </summary>
    public byte GetV4L2Fields()
    {
        return Field switch
        {
            H264PictureField.Frame => V4L2H264Constants.V4L2_H264_FRAME_REF,
            H264PictureField.TopField when OtherField != null =>
                (byte)(V4L2H264Constants.V4L2_H264_TOP_FIELD_REF | V4L2H264Constants.V4L2_H264_BOTTOM_FIELD_REF),
            H264PictureField.TopField => V4L2H264Constants.V4L2_H264_TOP_FIELD_REF,
            H264PictureField.BottomField when OtherField != null =>
                (byte)(V4L2H264Constants.V4L2_H264_TOP_FIELD_REF | V4L2H264Constants.V4L2_H264_BOTTOM_FIELD_REF),
            H264PictureField.BottomField => V4L2H264Constants.V4L2_H264_BOTTOM_FIELD_REF,
            _ => V4L2H264Constants.V4L2_H264_FRAME_REF
        };
    }

    /// <summary>
    /// Get the appropriate PicOrderCnt for this picture based on field type.
    /// </summary>
    public int GetPicOrderCnt()
    {
        return Field switch
        {
            H264PictureField.Frame => Math.Min(TopFieldOrderCnt, BottomFieldOrderCnt),
            H264PictureField.TopField => TopFieldOrderCnt,
            H264PictureField.BottomField => BottomFieldOrderCnt,
            _ => TopFieldOrderCnt
        };
    }

    /// <summary>
    /// Initialize picture from a parsed slice header.
    /// </summary>
    public void InitFromSliceHeader(SliceHeaderState header, SpsState sps, bool isIdr)
    {
        FrameNum = header.frame_num;
        NalRefIdc = (byte)header.nal_ref_idc;
        IsIdr = isIdr;
        IsRef = header.nal_ref_idc != 0;
        FieldPicFlag = header.field_pic_flag != 0;
        BottomFieldFlag = header.bottom_field_flag != 0;

        if (FieldPicFlag)
        {
            Field = BottomFieldFlag ? H264PictureField.BottomField : H264PictureField.TopField;
        }
        else
        {
            Field = H264PictureField.Frame;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Buffer lifecycle is managed separately by the decoder
        Buffer = null;
        OtherField = null;
    }
}
