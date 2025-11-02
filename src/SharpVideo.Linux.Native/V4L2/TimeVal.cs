using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// Struct for storing timestamp (equivalent to struct timeval in C)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TimeVal
{
    /// <summary>
    /// Seconds
    /// </summary>
    public long TvSec;

    /// <summary>
    /// Microseconds
    /// </summary>
    public long TvUsec;
}