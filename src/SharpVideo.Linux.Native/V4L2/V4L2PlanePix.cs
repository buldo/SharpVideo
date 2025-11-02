using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// Managed representation of the native <c>v4l2_plane_pix_format</c> structure.
/// Describes format information for a single plane in multiplanar pixel formats.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct V4L2PlanePix
{
    /// <summary>
    /// Maximum size in bytes required for data
    /// </summary>
    public uint SizeImage;

    /// <summary>
    /// Distance in bytes between the leftmost pixels in two adjacent lines
    /// </summary>
    public uint BytesPerLine;

    /// <summary>
    /// Reserved for future extensions
    /// </summary>
    public unsafe fixed ushort Reserved[6];
}