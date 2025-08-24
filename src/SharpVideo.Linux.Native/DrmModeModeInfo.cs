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
    public readonly uint Clock;

    /// <summary>
    /// Horizontal display size.
    /// </summary>
    public readonly ushort HDisplay;

    /// <summary>
    /// Horizontal sync start.
    /// </summary>
    public readonly ushort HSyncStart;

    /// <summary>
    /// Horizontal sync end.
    /// </summary>
    public readonly ushort HSyncEnd;

    /// <summary>
    /// Horizontal total.
    /// </summary>
    public readonly ushort HTotal;

    /// <summary>
    /// Horizontal skew.
    /// </summary>
    public readonly ushort HSkew;

    /// <summary>
    /// Vertical display size.
    /// </summary>
    public readonly ushort VDisplay;

    /// <summary>
    /// Vertical sync start.
    /// </summary>
    public readonly ushort VSyncStart;

    /// <summary>
    /// Vertical sync end.
    /// </summary>
    public readonly ushort VSyncEnd;

    /// <summary>
    /// Vertical total.
    /// </summary>
    public readonly ushort VTotal;

    /// <summary>
    /// Vertical scan.
    /// </summary>
    public readonly ushort VScan;

    /// <summary>
    /// Vertical refresh rate in Hz.
    /// </summary>
    public readonly uint VRefresh;

    /// <summary>
    /// Mode flags.
    /// </summary>
    public readonly uint Flags;

    /// <summary>
    /// Mode type.
    /// </summary>
    public readonly uint Type;

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