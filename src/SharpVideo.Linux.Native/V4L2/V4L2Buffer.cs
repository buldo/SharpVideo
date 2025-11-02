using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// Union for v4l2_buffer.m field (memory-specific data)
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public unsafe struct V4L2BufferM
{
    [FieldOffset(0)]
    public uint Offset;

    [FieldOffset(0)]
    public nuint Userptr;

    [FieldOffset(0)]
    public V4L2Plane* Planes;

    [FieldOffset(0)]
    public int Fd;
}

/// <summary>
/// Union for v4l2_buffer request field (request_fd or reserved)
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct V4L2BufferRequest
{
    [FieldOffset(0)]
    public int RequestFd;

    [FieldOffset(0)]
    public uint Reserved;
}

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
    public V4L2Memory Memory;

    /// <summary>
    /// Union for memory-specific data (offset, userptr, planes pointer, or fd)
    /// </summary>
    public V4L2BufferM M;

    /// <summary>
    /// Number of planes for multiplanar, buffer size for single-planar
    /// </summary>
    public uint Length;

    /// <summary>
    /// Reserved for future extensions
    /// </summary>
    public uint Reserved2;

    /// <summary>
    /// Union for request_fd or reserved field
    /// </summary>
    public V4L2BufferRequest Request;

    /// <summary>
    /// Helper property to access planes pointer
    /// </summary>
    public V4L2Plane* Planes
    {
        get => M.Planes;
        set => M.Planes = value;
    }

    /// <summary>
    /// Helper property to access request_fd
    /// </summary>
    public int RequestFd
    {
        get => Request.RequestFd;
        set => Request.RequestFd = value;
    }

    /// <summary>
    /// Gets planes as a span
    /// </summary>
    public Span<V4L2Plane> PlaneSpan
    {
        get
        {
            if (Planes == null || Length == 0)
                return Span<V4L2Plane>.Empty;
            return new Span<V4L2Plane>(Planes, (int)Length);
        }
    }
}