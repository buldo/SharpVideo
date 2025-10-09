namespace SharpVideo.V4L2StatelessDecoder.Models;

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

    /// <summary>
    /// Number of output buffers to allocate (for encoded data)
    /// </summary>
    public uint OutputBufferCount { get; init; } = 16;

    /// <summary>
    /// Number of capture buffers to allocate (for decoded frames)
    /// </summary>
    public uint CaptureBufferCount { get; init; } = 16;

    /// <summary>
    /// Number of media requests to keep allocated for reuse.
    /// </summary>
    public int RequestPoolSize { get; init; } = 32;
}