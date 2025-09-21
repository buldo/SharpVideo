using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>v4l2_streamparm</c> structure.
/// Stream parameters.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct V4L2StreamParm
{
    /// <summary>
    /// Stream type
    /// </summary>
    public uint Type;

    /// <summary>
    /// Capture parameters (when Type is capture)
    /// </summary>
    public V4L2CaptureParm Capture;

    // Note: In the actual C union, this would share memory with Capture
    // For simplicity in C#, we'll just use Capture for capture types
    // and handle output separately if needed
}