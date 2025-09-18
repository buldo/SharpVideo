using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

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

/// <summary>
/// Managed representation of the native <c>v4l2_pix_format_mplane</c> structure.
/// Describes multiplanar pixel format.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct V4L2PixFormatMplane
{
    /// <summary>
    /// Image width in pixels
    /// </summary>
    public uint Width;

    /// <summary>
    /// Image height in pixels
    /// </summary>
    public uint Height;

    /// <summary>
    /// Pixel format, usually a FOURCC code
    /// </summary>
    public uint PixelFormat;

    /// <summary>
    /// Field order
    /// </summary>
    public uint Field;

    /// <summary>
    /// Image colorspace
    /// </summary>
    public uint Colorspace;

    /// <summary>
    /// Per-plane format information (8 planes max)
    /// </summary>
    public fixed byte PlaneFormat[8 * 16]; // V4L2PlanePix[8], each is 16 bytes

    /// <summary>
    /// Number of planes
    /// </summary>
    public byte NumPlanes;

    /// <summary>
    /// Flags
    /// </summary>
    public byte Flags;

    /// <summary>
    /// Y'CbCr/HSV encoding (union in C)
    /// </summary>
    public byte YcbcrEncoding;

    /// <summary>
    /// Quantization
    /// </summary>
    public byte Quantization;

    /// <summary>
    /// Transfer function
    /// </summary>
    public byte XferFunc;

    /// <summary>
    /// Reserved for future extensions
    /// </summary>
    public fixed byte Reserved[7];

    /// <summary>
    /// Additional padding to match native structure size (192 - 160 = 32 bytes)
    /// </summary>
    private fixed byte _padding[32];

    /// <summary>
    /// Gets plane format information as a span
    /// </summary>
    public readonly Span<V4L2PlanePix> PlaneFormats
    {
        get
        {
            fixed (byte* ptr = PlaneFormat)
            {
                return new Span<V4L2PlanePix>(ptr, NumPlanes);
            }
        }
    }
}

/// <summary>
/// Managed representation of the native <c>v4l2_format</c> structure.
/// Describes data format.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct V4L2Format
{
    /// <summary>
    /// Buffer type
    /// </summary>
    public uint Type;

    /// <summary>
    /// Format data union - represented as raw bytes since it can contain different format types
    /// For multiplanar format, cast to V4L2PixFormatMplane
    /// </summary>
    public fixed byte FormatData[200]; // Union size is 200 bytes (largest member is raw_data[200])

    /// <summary>
    /// Padding to match native structure alignment (208 - 204 = 4 bytes)
    /// </summary>
    private uint _padding;

    /// <summary>
    /// Gets/sets the multiplanar format data when Type is multiplanar
    /// </summary>
    public V4L2PixFormatMplane Pix_mp
    {
        readonly get
        {
            fixed (byte* ptr = FormatData)
            {
                return *(V4L2PixFormatMplane*)ptr;
            }
        }
        set
        {
            fixed (byte* ptr = FormatData)
            {
                *(V4L2PixFormatMplane*)ptr = value;
            }
        }
    }
}