namespace SharpVideo.Decoding.V4l2.Discovery;

/// <summary>
/// Represents a discovered V4L2 H264 decoder device.
/// </summary>
public sealed class V4l2H264DecoderInfo
{
    /// <summary>
    /// The device path (e.g., /dev/video10).
    /// </summary>
    public required string DevicePath { get; init; }

    /// <summary>
    /// The type of decoder.
    /// </summary>
    public required V4l2H264DecoderType DecoderType { get; init; }

    /// <summary>
    /// The driver name.
    /// </summary>
    public required string Driver { get; init; }

    /// <summary>
    /// The card/device name.
    /// </summary>
    public required string Card { get; init; }

    /// <summary>
    /// Path to the associated media device (for stateless decoders), if found.
    /// </summary>
    public string? MediaDevicePath { get; init; }
}