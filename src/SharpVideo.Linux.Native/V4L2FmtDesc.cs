using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// V4L2 format flags
/// </summary>
[Flags]
public enum V4L2FormatFlags : uint
{
    /// <summary>
    /// Compressed format
    /// </summary>
    COMPRESSED = 0x0001,

    /// <summary>
    /// Emulated format by conversion
    /// </summary>
    EMULATED = 0x0002,

    /// <summary>
    /// Continuous frame field
    /// </summary>
    CONTINUOUS_BYTESTREAM = 0x0004,

    /// <summary>
    /// Dynamic allocation
    /// </summary>
    DYN_RESOLUTION = 0x0008,

    /// <summary>
    /// Encoding parameter changes without stream restart
    /// </summary>
    ENC_CAP_FRAME_INTERVAL = 0x0010,

    /// <summary>
    /// Flag to use CSC
    /// </summary>
    CSC_COLORSPACE = 0x0020,

    /// <summary>
    /// Flag to use CSC
    /// </summary>
    CSC_XFER_FUNC = 0x0040,

    /// <summary>
    /// Flag to use CSC
    /// </summary>
    CSC_YCBCR_ENC = 0x0080,

    /// <summary>
    /// Flag to use CSC
    /// </summary>
    CSC_HSV_ENC = CSC_YCBCR_ENC,

    /// <summary>
    /// Flag to use CSC
    /// </summary>
    CSC_QUANTIZATION = 0x0100,
}

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