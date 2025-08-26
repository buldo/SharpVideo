using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drmModeFB2</c> structure.
/// Contains extended framebuffer information with per-plane configuration.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DrmModeFB2
{
    /// <summary>
    /// Framebuffer ID.
    /// </summary>
    public uint FbId;

    /// <summary>
    /// Framebuffer width in pixels.
    /// </summary>
    public uint Width;

    /// <summary>
    /// Framebuffer height in pixels.
    /// </summary>
    public uint Height;

    /// <summary>
    /// Pixel format (fourcc code from drm_fourcc.h).
    /// </summary>
    public uint PixelFormat;

    /// <summary>
    /// Format modifier (applies to all buffers).
    /// </summary>
    public ulong Modifier;

    /// <summary>
    /// Framebuffer flags.
    /// </summary>
    public uint Flags;

    /// <summary>
    /// Per-plane GEM handles (may be duplicate entries for multiple planes).
    /// </summary>
    public fixed uint Handles[4];

    /// <summary>
    /// Per-plane pitches in bytes.
    /// </summary>
    public fixed uint Pitches[4];

    /// <summary>
    /// Per-plane offsets in bytes.
    /// </summary>
    public fixed uint Offsets[4];
}