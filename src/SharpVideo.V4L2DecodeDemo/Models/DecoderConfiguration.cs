namespace SharpVideo.V4L2DecodeDemo.Models;

/// <summary>
/// Configuration settings for the H.264 decoder
/// </summary>
public class DecoderConfiguration
{
    /// <summary>
    /// Size of data chunks to read from input file
    /// </summary>
    public int ChunkSize { get; init; } = 32768; // 32KB

    /// <summary>
    /// Number of output buffers for encoded data
    /// </summary>
    public int OutputBufferCount { get; init; } = 8;

    /// <summary>
    /// Number of capture buffers for decoded frames
    /// </summary>
    public int CaptureBufferCount { get; init; } = 16;

    /// <summary>
    /// Maximum number of devices to scan
    /// </summary>
    public int MaxDeviceScan { get; init; } = 64;

    /// <summary>
    /// Timeout for device operations in milliseconds
    /// </summary>
    public int DeviceTimeoutMs { get; init; } = 5000;

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
    /// Alternative output pixel format
    /// </summary>
    public uint AlternativePixelFormat { get; init; } = 0x32315559; // YUV420
}