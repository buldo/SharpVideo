using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drmModeModeInfo</c> structure.
/// Contains detailed information about a display mode.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DrmModeModeInfo
{
    /// <summary>
    /// Pixel clock in kHz.
    /// </summary>
    public uint Clock;

    /// <summary>
    /// Horizontal display size.
    /// </summary>
    public ushort HDisplay;

    /// <summary>
    /// Horizontal sync start.
    /// </summary>
    public ushort HSyncStart;

    /// <summary>
    /// Horizontal sync end.
    /// </summary>
    public ushort HSyncEnd;

    /// <summary>
    /// Horizontal total.
    /// </summary>
    public ushort HTotal;

    /// <summary>
    /// Horizontal skew.
    /// </summary>
    public ushort HSkew;

    /// <summary>
    /// Vertical display size.
    /// </summary>
    public ushort VDisplay;

    /// <summary>
    /// Vertical sync start.
    /// </summary>
    public ushort VSyncStart;

    /// <summary>
    /// Vertical sync end.
    /// </summary>
    public ushort VSyncEnd;

    /// <summary>
    /// Vertical total.
    /// </summary>
    public ushort VTotal;

    /// <summary>
    /// Vertical scan.
    /// </summary>
    public ushort VScan;

    /// <summary>
    /// Vertical refresh rate in Hz.
    /// </summary>
    public uint VRefresh;

    /// <summary>
    /// Mode flags.
    /// </summary>
    public DrmModeFlag Flags;

    /// <summary>
    /// Mode type.
    /// </summary>
    public DrmModeType Type;

    /// <summary>
    /// Mode name (32 characters).
    /// </summary>
    public fixed byte Name[32];

    /// <summary>
    /// Gets the mode name as a string.
    /// </summary>
    public string NameString
    {
        get
        {
            fixed (byte* namePtr = Name)
            {
                return System.Text.Encoding.UTF8.GetString(namePtr, 32).TrimEnd('\0');
            }
        }
    }
}

/// <summary>
/// Video mode flags
/// bit compatible with the xrandr RR_ definitions (bits 0-13)
///
/// ABI warning: Existing userspace really expects
/// the mode flags to match the xrandr definitions. Any
/// changes that don't match the xrandr definitions will
/// likely need a new client cap or some other mechanism
/// to avoid breaking existing userspace. This includes
/// allocating new flags in the previously unused bits!
/// </summary>
[Flags]
public enum DrmModeFlag : uint
{
    DRM_MODE_FLAG_PHSYNC = (1 << 0),
    DRM_MODE_FLAG_NHSYNC = (1 << 1),
    DRM_MODE_FLAG_PVSYNC = (1 << 2),
    DRM_MODE_FLAG_NVSYNC = (1 << 3),
    DRM_MODE_FLAG_INTERLACE = (1 << 4),
    DRM_MODE_FLAG_DBLSCAN = (1 << 5),
    DRM_MODE_FLAG_CSYNC = (1 << 6),
    DRM_MODE_FLAG_PCSYNC = (1 << 7),
    DRM_MODE_FLAG_NCSYNC = (1 << 8),
    DRM_MODE_FLAG_HSKEW = (1 << 9), /* hskew provided */
    DRM_MODE_FLAG_BCAST = (1 << 10), /* deprecated */
    DRM_MODE_FLAG_PIXMUX = (1 << 11), /* deprecated */
    DRM_MODE_FLAG_DBLCLK = (1 << 12),
    DRM_MODE_FLAG_CLKDIV2 = (1 << 13),

    /*
     * When adding a new stereo mode don't forget to adjust DRM_MODE_FLAGS_3D_MAX
     * (define not exposed to user space).
     */
    DRM_MODE_FLAG_3D_MASK = (0x1f << 14),
    DRM_MODE_FLAG_3D_NONE = (0 << 14),
    DRM_MODE_FLAG_3D_FRAME_PACKING = (1 << 14),
    DRM_MODE_FLAG_3D_FIELD_ALTERNATIVE = (2 << 14),
    DRM_MODE_FLAG_3D_LINE_ALTERNATIVE = (3 << 14),
    DRM_MODE_FLAG_3D_SIDE_BY_SIDE_FULL = (4 << 14),
    DRM_MODE_FLAG_3D_L_DEPTH = (5 << 14),
    DRM_MODE_FLAG_3D_L_DEPTH_GFX_GFX_DEPTH = (6 << 14),
    DRM_MODE_FLAG_3D_TOP_AND_BOTTOM = (7 << 14),
    DRM_MODE_FLAG_3D_SIDE_BY_SIDE_HALF = (8 << 14),
}