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