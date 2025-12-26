namespace SharpVideo.Utils.Buffers;

/// <summary>
/// Buffer parameters for DRM framebuffer allocation and drmModeAddFB2 calls.
/// </summary>
public class BufferParams
{
    /// <summary>
    /// Buffer parameters for DRM framebuffer allocation and drmModeAddFB2 calls.
    /// </summary>
    /// <param name="width">Framebuffer width in pixels.</param>
    /// <param name="height">Framebuffer height in pixels.</param>
    /// <param name="totalSize">Total buffer size required for allocation (all planes).</param>
    /// <param name="planes">Per-plane information (pitch, offset, size).</param>
    public BufferParams(
        uint width,
        uint height,
        uint totalSize,
        IReadOnlyList<PlaneInfo> planes)
    {
        Width = width;
        Height = height;
        TotalSize = totalSize;
        Planes = planes;
    }

    /// <summary>
    /// Number of planes in the buffer.
    /// </summary>
    public int PlanesCount => Planes.Count;

    /// <summary>
    /// Primary plane pitch (stride) - typically used for single-plane formats.
    /// </summary>
    public uint Stride => Planes.Count > 0 ? Planes[0].Pitch : 0;

    /// <summary>Framebuffer width in pixels.</summary>
    public uint Width { get; }

    /// <summary>Framebuffer height in pixels.</summary>
    public uint Height { get; }

    /// <summary>Total buffer size required for allocation (all planes).</summary>
    public uint TotalSize { get; }

    /// <summary>Per-plane information (pitch, offset, size).</summary>
    public IReadOnlyList<PlaneInfo> Planes { get; }
}