namespace SharpVideo.V4L2DecodeDemo.Models;

/// <summary>
/// Information about a V4L2 device
/// </summary>
public record V4L2DeviceInfo
{
    public required string DevicePath { get; init; }
    public required string DriverName { get; init; }
    public required string CardName { get; init; }
    public required uint DeviceCapabilities { get; init; }
    public required IReadOnlyList<V4L2FormatInfo> SupportedFormats { get; init; }
    public bool SupportsH264 { get; init; }
    public bool IsMemoryToMemoryDevice { get; init; }
}

/// <summary>
/// Information about a V4L2 format
/// </summary>
public record V4L2FormatInfo
{
    public required uint Index { get; init; }
    public required uint PixelFormat { get; init; }
    public required string Description { get; init; }
    public required uint BufferType { get; init; }
}