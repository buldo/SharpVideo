using System.Runtime.Versioning;

namespace SharpVideo.V4L2DecodeDemo.Services;

/// <summary>
/// Represents a mapped V4L2 buffer for stateless decoding operations
/// </summary>
[SupportedOSPlatform("linux")]
public class MappedBuffer
{
    /// <summary>
    /// Buffer index in the V4L2 queue
    /// </summary>
    public required uint Index { get; init; }

    /// <summary>
    /// Pointers to the mapped memory regions per plane
    /// </summary>
    public required IntPtr[] PlanePointers { get; init; }

    /// <summary>
    /// Sizes of the mapped planes in bytes
    /// </summary>
    public required uint[] PlaneSizes { get; init; }

    /// <summary>
    /// V4L2 planes associated with this buffer
    /// </summary>
    public required Linux.Native.V4L2Plane[] Planes { get; init; }

    /// <summary>
    /// Primary plane pointer (plane 0) for convenience.
    /// </summary>
    public IntPtr Pointer => PlanePointers.Length > 0 ? PlanePointers[0] : IntPtr.Zero;

    /// <summary>
    /// Size of the primary plane (plane 0) for convenience.
    /// </summary>
    public uint Size => PlaneSizes.Length > 0 ? PlaneSizes[0] : 0;

    public Span<byte> AsSpan()
    {
        unsafe
        {
            return new Span<byte>((void*)Pointer, (int)Size);
        }
    }
}