using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native.V4L2;

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
    public fixed byte PlaneFormat[8 * 20]; // V4L2PlanePix[8], each is 20 bytes

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