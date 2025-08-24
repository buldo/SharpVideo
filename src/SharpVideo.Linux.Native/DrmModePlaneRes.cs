using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drmModePlaneRes</c> structure.
/// Contains plane resource information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe readonly struct DrmModePlaneRes
{
    /// <summary>
    /// Number of planes.
    /// </summary>
    public readonly uint CountPlanes;

    /// <summary>
    /// Pointer to plane IDs array.
    /// </summary>
    private readonly uint* _planes;

    /// <summary>
    /// Gets a span over the plane IDs.
    /// </summary>
    public ReadOnlySpan<uint> Planes => _planes == null
        ? ReadOnlySpan<uint>.Empty
        : new ReadOnlySpan<uint>(_planes, (int)CountPlanes);
}