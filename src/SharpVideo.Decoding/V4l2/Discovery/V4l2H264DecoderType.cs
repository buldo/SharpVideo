namespace SharpVideo.Decoding.V4l2.Discovery;

/// <summary>
/// Describes the type of V4L2 H264 decoder detected.
/// </summary>
public enum V4l2H264DecoderType
{
    /// <summary>
    /// No V4L2 H264 decoder found.
    /// </summary>
    None,

    /// <summary>
    /// Stateless H264 decoder (requires userspace to manage decoding state).
    /// Examples: Raspberry Pi, Rockchip RK3588.
    /// </summary>
    Stateless,

    /// <summary>
    /// Stateful H264 decoder (decoder manages state internally).
    /// Examples: Qualcomm Venus, some other SoCs.
    /// </summary>
    Stateful
}