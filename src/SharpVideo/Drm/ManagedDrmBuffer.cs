using System.Runtime.Versioning;

namespace SharpVideo.Drm;

/// <summary>
/// Represents a managed DRM buffer with associated metadata.
/// </summary>
[SupportedOSPlatform("linux")]
public class ManagedDrmBuffer : IDisposable
{
    public required DmaBuffers.DmaBuffer DmaBuffer { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public uint Format { get; init; }
    public int Index { get; init; }
    public uint Stride { get; init; } // BytesPerLine - actual stride including padding
    public uint DrmHandle { get; set; }
    public uint FramebufferId { get; set; }

    public void Dispose()
    {
        DmaBuffer.UnmapBuffer();
        DmaBuffer.Dispose();
    }
}