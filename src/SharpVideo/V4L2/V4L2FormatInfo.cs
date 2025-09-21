using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

public record V4L2FormatInfo
{
    public required uint Index { get; init; }
    public required uint PixelFormat { get; init; }
    public required string Description { get; init; }
    public required V4L2BufferType BufferType { get; init; }
}