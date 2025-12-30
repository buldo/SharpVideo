using SharpVideo.Drm;

namespace SharpVideo.Decoding.V4l2;

/// <summary>
/// Configuration settings for V4L2 H264 decoders.
/// </summary>
public sealed class V4l2DecoderConfiguration
{
    /// <summary>
    /// Initial video width in pixels (may be updated by decoder based on stream).
    /// </summary>
    public uint InitialWidth { get; init; } = 1920;

    /// <summary>
    /// Initial video height in pixels (may be updated by decoder based on stream).
    /// </summary>
    public uint InitialHeight { get; init; } = 1080;

    /// <summary>
    /// Preferred output pixel format (e.g., NV12 = 0x3231564E).
    /// </summary>
    public uint PreferredPixelFormat { get; init; } = 0x3231564E; // NV12

    /// <summary>
    /// Number of output buffers for encoded data (input to decoder).
    /// </summary>
    public uint OutputBufferCount { get; init; } = 16;

    /// <summary>
    /// Number of capture buffers for decoded frames (output from decoder).
    /// </summary>
    public uint CaptureBufferCount { get; init; } = 16;

    /// <summary>
    /// Number of media requests to pool for stateless decoders.
    /// </summary>
    public int RequestPoolSize { get; init; } = 32;

    /// <summary>
    /// Use DRM PRIME buffers for zero-copy decoding.
    /// When true, decoded frames can be directly displayed without copying.
    /// </summary>
    public bool UseDrmPrimeBuffers { get; init; }

    /// <summary>
    /// Gets the preferred output pixel format as a PixelFormat.
    /// </summary>
    public PixelFormat GetPixelFormat() => new(PreferredPixelFormat);
}
