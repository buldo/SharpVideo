using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>v4l2_fmtdesc</c> structure.
/// Used to describe a format supported by the device.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct V4L2FmtDesc
{
    /// <summary>
    /// Format number, set by the application
    /// </summary>
    public uint Index;

    /// <summary>
    /// Buffer type
    /// </summary>
    public uint Type;

    /// <summary>
    /// Format flags
    /// </summary>
    public V4L2FormatFlags Flags;

    /// <summary>
    /// Format description
    /// </summary>
    public fixed byte Description[32];

    /// <summary>
    /// Format identifier (FOURCC)
    /// </summary>
    public uint PixelFormat;

    /// <summary>
    /// Media bus code for CSI/parallel interfaces
    /// </summary>
    public uint MBusCode;

    /// <summary>
    /// Reserved for future extensions
    /// </summary>
    public fixed uint Reserved[3];

    /// <summary>
    /// Gets the description as a managed string
    /// </summary>
    public readonly string DescriptionString
    {
        get
        {
            fixed (byte* ptr = Description)
            {
                return Marshal.PtrToStringUTF8((nint)ptr) ?? string.Empty;
            }
        }
    }
}