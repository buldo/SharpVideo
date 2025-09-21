using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

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