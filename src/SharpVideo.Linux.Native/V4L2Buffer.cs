using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>v4l2_buffer</c> structure.
/// Describes a video buffer.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct V4L2Buffer
{
    /// <summary>
    /// Buffer index
    /// </summary>
    public uint Index;

    /// <summary>
    /// Buffer type
    /// </summary>
    public V4L2BufferType Type;

    /// <summary>
    /// Number of bytes occupied by data in the buffer (unused for multiplanar)
    /// </summary>
    public uint BytesUsed;

    /// <summary>
    /// Buffer flags
    /// </summary>
    public uint Flags;

    /// <summary>
    /// Field order
    /// </summary>
    public uint Field;

    /// <summary>
    /// Time stamp
    /// </summary>
    public TimeVal Timestamp;

    /// <summary>
    /// Time code
    /// </summary>
    public V4L2Timecode Timecode;

    /// <summary>
    /// Frame sequence number
    /// </summary>
    public uint Sequence;

    /// <summary>
    /// Memory type
    /// </summary>
    public uint Memory;

    /// <summary>
    /// Planes data - for multiplanar buffers
    /// </summary>
    public V4L2Plane* Planes;

    /// <summary>
    /// Number of planes for multiplanar, buffer size for single-planar
    /// </summary>
    public uint Length;

    /// <summary>
    /// Reserved for future extensions
    /// </summary>
    public uint Reserved2;

    /// <summary>
    /// Request file descriptor
    /// </summary>
    public int RequestFd;

    /// <summary>
    /// Gets planes as a span
    /// </summary>
    public readonly Span<V4L2Plane> PlaneSpan
    {
        get
        {
            if (Planes == null || Length == 0)
                return Span<V4L2Plane>.Empty;
            return new Span<V4L2Plane>(Planes, (int)Length);
        }
    }
}