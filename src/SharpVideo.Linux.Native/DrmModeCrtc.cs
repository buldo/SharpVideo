using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drmModeCrtc</c> structure.
/// Contains CRTC (display controller) information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DrmModeCrtc
{
    /// <summary>
    /// CRTC ID.
    /// </summary>
    public readonly uint CrtcId;

    /// <summary>
    /// Current framebuffer ID.
    /// </summary>
    public readonly uint BufferId;

    /// <summary>
    /// X position of the CRTC.
    /// </summary>
    public readonly uint X;

    /// <summary>
    /// Y position of the CRTC.
    /// </summary>
    public readonly uint Y;

    /// <summary>
    /// Width of the CRTC.
    /// </summary>
    public readonly uint Width;

    /// <summary>
    /// Height of the CRTC.
    /// </summary>
    public readonly uint Height;

    /// <summary>
    /// Whether the mode is valid.
    /// </summary>
    public readonly int ModeValid;

    /// <summary>
    /// Current mode information.
    /// </summary>
    public readonly DrmModeModeInfo Mode;

    /// <summary>
    /// Gamma table size.
    /// </summary>
    public readonly int GammaSize;
}