namespace SharpVideo.Decoding.V4l2.H264;

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
    public uint PicOrderCnt { get; set; }

    /// <summary>
    /// True if this frame is used as a reference for other frames.
    /// </summary>
    public bool IsReference { get; set; }

    /// <summary>
    /// True if this is a long-term reference frame.
    /// </summary>
    public bool IsLongTerm { get; set; }
}
