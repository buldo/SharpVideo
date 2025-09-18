using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>v4l2_plane</c> structure.
/// Describes a single plane for multiplanar buffers.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct V4L2Plane
{
    /// <summary>
    /// Number of bytes occupied by data in the plane
    /// </summary>
    public uint BytesUsed;

    /// <summary>
    /// Size in bytes of the plane
    /// </summary>
    public uint Length;

    /// <summary>
    /// Memory union - for DMABUF, this is the fd
    /// For mmap: mem_offset
    /// For userptr: userptr (unsigned long)
    /// For dmabuf: fd
    /// </summary>
    public int Fd; // We'll use fd for DMABUF case

    /// <summary>
    /// Offset from the start of the device memory for this plane
    /// </summary>
    public uint DataOffset;

    /// <summary>
    /// Reserved for future extensions
    /// </summary>
    public fixed uint Reserved[11];
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
    public uint Type;

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
    /// Time stamp (seconds)
    /// </summary>
    public long TimestampSec;

    /// <summary>
    /// Time stamp (microseconds)
    /// </summary>
    public long TimestampUsec;

    /// <summary>
    /// Time code type
    /// </summary>
    public uint TimecodeType;

    /// <summary>
    /// Time code flags
    /// </summary>
    public uint TimecodeFlags;

    /// <summary>
    /// Time code frames
    /// </summary>
    public byte TimecodeFrames;

    /// <summary>
    /// Time code seconds
    /// </summary>
    public byte TimecodeSeconds;

    /// <summary>
    /// Time code minutes
    /// </summary>
    public byte TimecodeMinutes;

    /// <summary>
    /// Time code hours
    /// </summary>
    public byte TimecodeHours;

    /// <summary>
    /// Time code user bits
    /// </summary>
    public fixed byte TimecodeUserbits[4];

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

/// <summary>
/// Managed representation of the native <c>v4l2_requestbuffers</c> structure.
/// Used to request buffer allocation.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct V4L2RequestBuffers
{
    /// <summary>
    /// Number of buffers requested/allocated
    /// </summary>
    public uint Count;

    /// <summary>
    /// Buffer type
    /// </summary>
    public uint Type;

    /// <summary>
    /// Memory type
    /// </summary>
    public uint Memory;

    /// <summary>
    /// Driver capability flags
    /// </summary>
    public uint Capabilities;

    /// <summary>
    /// Request flags
    /// </summary>
    public byte Flags;

    /// <summary>
    /// Reserved for future extensions
    /// </summary>
    public fixed byte Reserved[3];
}

/// <summary>
/// Managed representation of the native <c>v4l2_exportbuffer</c> structure.
/// Used to export buffer as DMABUF file descriptor.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct V4L2ExportBuffer
{
    /// <summary>
    /// Buffer type
    /// </summary>
    public uint Type;

    /// <summary>
    /// Buffer index
    /// </summary>
    public uint Index;

    /// <summary>
    /// Plane index for multiplanar buffers
    /// </summary>
    public uint Plane;

    /// <summary>
    /// Export flags
    /// </summary>
    public uint Flags;

    /// <summary>
    /// DMABUF file descriptor (returned by the driver)
    /// </summary>
    public int Fd;

    /// <summary>
    /// Reserved for future extensions
    /// </summary>
    public fixed uint Reserved[11];
}