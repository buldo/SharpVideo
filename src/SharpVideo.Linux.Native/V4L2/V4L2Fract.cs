using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// Managed representation of the native <c>v4l2_fract</c> structure.
/// Represents a fraction.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct V4L2Fract
{
    /// <summary>
    /// Numerator
    /// </summary>
    public uint Numerator;

    /// <summary>
    /// Denominator
    /// </summary>
    public uint Denominator;
}