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
    /// Alternative output pixel format (fallback)
    /// </summary>
    public uint AlternativePixelFormat { get; init; } = 0x32315559; // YUV420

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
    /// Maximum file read chunk size (in bytes)
    /// </summary>
    public uint MaxReadChunkSize { get; init; } = 1024 * 1024; // 1MB

    /// <summary>
    /// Whether to use start codes in NALU data
    /// </summary>
    public bool UseStartCodes { get; init; } = true;

    /// <summary>
    /// Enable verbose logging for debugging
    /// </summary>
    public bool VerboseLogging { get; init; } = false;

    /// <summary>
    /// Progress reporting interval (report every N processed NALUs)
    /// </summary>
    public int ProgressReportInterval { get; init; } = 10;
}