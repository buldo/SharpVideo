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