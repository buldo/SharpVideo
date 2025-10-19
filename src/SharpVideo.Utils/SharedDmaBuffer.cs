using System.Runtime.Versioning;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.V4L2;

namespace SharpVideo.Utils;

/// <summary>
/// Represents a managed DRM buffer with associated metadata.
/// </summary>
[SupportedOSPlatform("linux")]
public class SharedDmaBuffer : IDisposable
{
    public required DmaBuffer DmaBuffer { get; init; }
    public required uint Width { get; init; }
    public required uint Height { get; init; }
    public required uint Stride { get; init; }
    public required PixelFormat Format { get; init; }
    public uint FramebufferId { get; set; }
    public void MapBuffer() => DmaBuffer.MapBuffer();
    public MapStatus MapStatus => DmaBuffer.MapStatus;
    public V4L2DmaBufMPlaneBuffer V4L2Buffer { get; set; }

    public void Dispose()
    {
        DmaBuffer.UnmapBuffer();
        DmaBuffer.Dispose();
    }
}