using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

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