using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drmModeFB</c> structure.
/// Contains framebuffer information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct DrmModeFB
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
    /// Framebuffer pitch (bytes per row).
    /// </summary>
    public readonly uint Pitch;

    /// <summary>
    /// Bits per pixel.
    /// </summary>
    public readonly uint Bpp;

    /// <summary>
    /// Color depth.
    /// </summary>
    public readonly uint Depth;

    /// <summary>
    /// Driver-specific handle.
    /// </summary>
    public readonly uint Handle;
}