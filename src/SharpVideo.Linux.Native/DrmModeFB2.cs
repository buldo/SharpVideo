using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drmModeFB2</c> structure.
/// Contains extended framebuffer information with per-plane configuration.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct DrmModeFB2
{
    /// <summary>
    /// Framebuffer ID.
    /// </summary>
    public readonly uint FbId;

    /// <summary>
    /// Framebuffer width in pixels.
    /// </summary>
    public readonly uint Width;

    /// <summary>
    /// Framebuffer height in pixels.
    /// </summary>
    public readonly uint Height;

    /// <summary>
    /// Pixel format (fourcc code from drm_fourcc.h).
    /// </summary>
    public readonly uint PixelFormat;

    /// <summary>
    /// Format modifier (applies to all buffers).
    /// </summary>
    public readonly ulong Modifier;

    /// <summary>
    /// Framebuffer flags.
    /// </summary>
    public readonly uint Flags;

    /// <summary>
    /// Per-plane GEM handles (may be duplicate entries for multiple planes).
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public readonly uint[] Handles;

    /// <summary>
    /// Per-plane pitches in bytes.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public readonly uint[] Pitches;

    /// <summary>
    /// Per-plane offsets in bytes.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public readonly uint[] Offsets;
}