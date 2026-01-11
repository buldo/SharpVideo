namespace SharpVideo.Decoding.V4l2.H264;

using SharpVideo.Utils;
using SharpVideo.Linux.Native.V4L2;

/// <summary>
/// Represents an entry in the Decoded Picture Buffer (DPB) for H264 decoding.
/// </summary>
internal sealed class DpbEntry
{
    /// <summary>
    /// The frame number from the slice header.
    /// </summary>
    public uint FrameNum { get; set; }

    /// <summary>
    /// Picture order count value.
    /// </summary>
    public int PicOrderCnt { get; set; }

    /// <summary>
    /// True if this frame is used as a reference for other frames.
    /// </summary>
    public bool IsReference { get; set; }

    /// <summary>
    /// True if this is a long-term reference frame.
    /// </summary>
    public bool IsLongTerm { get; set; }

    /// <summary>
    /// V4L2 timestamp for reference identification.
    /// </summary>
    public ulong Timestamp { get; set; }

    /// <summary>
    /// Field reference flags (V4L2_H264_*_REF), used by slice reference lists.
    /// </summary>
    public byte Fields { get; set; } = V4L2H264Constants.V4L2_H264_FRAME_REF;

    /// <summary>
    /// Back-reference to the underlying capture buffer so we can track buffer reuse.
    /// </summary>
    public required SharedDmaBuffer Buffer { get; set; }
}
