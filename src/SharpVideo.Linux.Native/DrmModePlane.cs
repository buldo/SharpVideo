using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drmModePlane</c> structure.
/// Contains plane information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct DrmModePlane
{
    /// <summary>
    /// Number of supported formats.
    /// </summary>
    public readonly uint CountFormats;

    /// <summary>
    /// Pointer to supported formats array.
    /// </summary>
    private readonly uint* _formats;

    /// <summary>
    /// Plane ID.
    /// </summary>
    public readonly uint PlaneId;

    /// <summary>
    /// Current CRTC ID.
    /// </summary>
    public readonly uint CrtcId;

    /// <summary>
    /// Current framebuffer ID.
    /// </summary>
    public readonly uint FbId;

    /// <summary>
    /// X position on CRTC.
    /// </summary>
    public readonly uint CrtcX;

    /// <summary>
    /// Y position on CRTC.
    /// </summary>
    public readonly uint CrtcY;

    /// <summary>
    /// X position in framebuffer.
    /// </summary>
    public readonly uint X;

    /// <summary>
    /// Y position in framebuffer.
    /// </summary>
    public readonly uint Y;

    /// <summary>
    /// Bitmask of possible CRTCs.
    /// </summary>
    public readonly uint PossibleCrtcs;

    /// <summary>
    /// Gamma table size.
    /// </summary>
    public readonly uint GammaSize;

    /// <summary>
    /// Gets a span over the supported formats.
    /// </summary>
    public ReadOnlySpan<uint> Formats => _formats == null
        ? ReadOnlySpan<uint>.Empty
        : new ReadOnlySpan<uint>(_formats, (int)CountFormats);
}