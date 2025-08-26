using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drmModePlane</c> structure.
/// Contains plane information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DrmModePlane
{
    /// <summary>
    /// Number of supported formats.
    /// </summary>
    public uint CountFormats;

    /// <summary>
    /// Pointer to supported formats array.
    /// </summary>
    public uint* FormatsPtr;

    /// <summary>
    /// Plane ID.
    /// </summary>
    public uint PlaneId;

    /// <summary>
    /// Current CRTC ID.
    /// </summary>
    public uint CrtcId;

    /// <summary>
    /// Current framebuffer ID.
    /// </summary>
    public uint FbId;

    /// <summary>
    /// X position on CRTC.
    /// </summary>
    public uint CrtcX;

    /// <summary>
    /// Y position on CRTC.
    /// </summary>
    public uint CrtcY;

    /// <summary>
    /// X position in framebuffer.
    /// </summary>
    public uint X;

    /// <summary>
    /// Y position in framebuffer.
    /// </summary>
    public uint Y;

    /// <summary>
    /// Bitmask of possible CRTCs.
    /// </summary>
    public uint PossibleCrtcs;

    /// <summary>
    /// Gamma table size.
    /// </summary>
    public uint GammaSize;

    /// <summary>
    /// Gets a span over the supported formats.
    /// </summary>
    public readonly ReadOnlySpan<uint> Formats => FormatsPtr == null
        ? ReadOnlySpan<uint>.Empty
        : new ReadOnlySpan<uint>(FormatsPtr, (int)CountFormats);
}