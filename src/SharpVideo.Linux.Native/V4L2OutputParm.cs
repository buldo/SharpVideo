using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>v4l2_outputparm</c> structure.
/// Output parameters.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct V4L2OutputParm
{
    /// <summary>
    /// Output capability flags
    /// </summary>
    public uint Capability;

    /// <summary>
    /// Output mode flags
    /// </summary>
    public uint OutputMode;

    /// <summary>
    /// Time per frame in seconds
    /// </summary>
    public V4L2Fract TimePerFrame;

    /// <summary>
    /// Extended flags
    /// </summary>
    public uint ExtendedMode;

    /// <summary>
    /// # of buffers for write
    /// </summary>
    public uint WriteBuffers;

    /// <summary>
    /// Reserved for future extensions
    /// </summary>
    public fixed uint Reserved[4];
}