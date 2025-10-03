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

    /// <summary>
    /// Number of output buffers for slice data
    /// </summary>
    public uint OutputBufferCount { get; init; } = 4;

    /// <summary>
    /// Number of capture buffers for decoded frames
    /// </summary>
    public uint CaptureBufferCount { get; init; } = 4;

    /// <summary>
    /// Buffer size for slice data (in bytes)
    /// </summary>
    public uint SliceBufferSize { get; init; } = 1024 * 1024; // 1MB

    /// <summary>
    /// Optional media controller device path to enable request API.
    /// </summary>
    public string? MediaDevicePath { get; init; } = null; // Disabled by default for better compatibility

    /// <summary>
    /// Number of media requests to keep allocated for reuse.
    /// </summary>
    public uint RequestPoolSize { get; init; } = 8;
}