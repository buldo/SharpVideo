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
    /// Memory descriptor (union in native struct).
    /// </summary>
    public PlaneMemory Memory;

    /// <summary>
    /// Offset from the start of the captured data for this plane.
    /// </summary>
    public uint DataOffset;

    /// <summary>
    /// Reserved for future extensions
    /// </summary>
    public fixed uint Reserved[11];

    /// <summary>
    /// Representation of the m union in v4l2_plane.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct PlaneMemory
    {
        /// <summary>
        /// Offset within the device buffer (MMAP).
        /// </summary>
        [FieldOffset(0)]
        public uint MemOffset;

        /// <summary>
        /// Userspace pointer (USERPTR).
        /// </summary>
        [FieldOffset(0)]
        public nuint UserPtr;

        /// <summary>
        /// DMA-BUF file descriptor (DMABUF).
        /// </summary>
        [FieldOffset(0)]
        public int Fd;
    }
}