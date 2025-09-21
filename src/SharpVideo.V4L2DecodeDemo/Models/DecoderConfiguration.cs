namespace SharpVideo.V4L2DecodeDemo.Models;

/// <summary>
/// Configuration settings for the H.264 decoder
/// </summary>
public class DecoderConfiguration
{
    /// <summary>
    /// Initial video width (will be updated by decoder)
    /// </summary>
    public uint InitialWidth { get; init; } = 1920;

    /// <summary>
    /// Initial video height (will be updated by decoder)
    /// </summary>
    public uint InitialHeight { get; init; } = 1080;

    /// <summary>
    /// Preferred output pixel format
    /// </summary>
    public uint PreferredPixelFormat { get; init; } = 0x3231564E; // NV12
}