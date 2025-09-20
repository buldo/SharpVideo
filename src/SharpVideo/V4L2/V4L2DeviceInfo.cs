using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

public record V4L2DeviceInfo
{
    public required string DevicePath { get; init; }
    public required string DriverName { get; init; }
    public required string CardName { get; init; }
    public required V4L2Capabilities DeviceCapabilities { get; init; }
    public required IReadOnlyList<V4L2FormatInfo> SupportedFormats { get; init; }
    public bool IsMemoryToMemoryDevice { get; init; }
}