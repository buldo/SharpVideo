using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

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
    public V4L2BufferType Type;

    /// <summary>
    /// Memory type
    /// </summary>
    public V4L2Memory Memory;

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