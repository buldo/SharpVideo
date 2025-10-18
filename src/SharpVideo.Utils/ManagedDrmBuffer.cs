using System.Runtime.Versioning;
using SharpVideo.DmaBuffers;

namespace SharpVideo.Drm;

/// <summary>
/// Represents a managed DRM buffer with associated metadata.
/// </summary>
[SupportedOSPlatform("linux")]
public class ManagedDrmBuffer : IDisposable
{
    public required DmaBuffers.DmaBuffer DmaBuffer { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    public required uint Stride { get; init; }

    public required PixelFormat Format { get; init; }
    public uint FramebufferId { get; set; }

    public void MapBuffer() => DmaBuffer.MapBuffer();
    public MapStatus MapStatus => DmaBuffer.MapStatus;

    public void Dispose()
    {
        DmaBuffer.UnmapBuffer();
        DmaBuffer.Dispose();
    }
}