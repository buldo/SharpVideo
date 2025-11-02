using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// Managed representation of the native <c>v4l2_captureparm</c> structure.
/// Capture parameters.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct V4L2CaptureParm
{
    /// <summary>
    /// Capture capability flags
    /// </summary>
    public uint Capability;

    /// <summary>
    /// Capture mode flags
    /// </summary>
    public uint CaptureMode;

    /// <summary>
    /// Time per frame in seconds
    /// </summary>
    public V4L2Fract TimePerFrame;

    /// <summary>
    /// Extended flags
    /// </summary>
    public uint ExtendedMode;

    /// <summary>
    /// # of buffers for read
    /// </summary>
    public uint ReadBuffers;

    /// <summary>
    /// Reserved for future extensions
    /// </summary>
    public fixed uint Reserved[4];
}