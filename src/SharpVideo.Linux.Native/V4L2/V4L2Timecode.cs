using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// Struct for storing timecode (equivalent to struct v4l2_timecode in C)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct V4L2Timecode
{
    /// <summary>
    /// Type of the time code
    /// </summary>
    public uint Type;

    /// <summary>
    /// Flags for the time code
    /// </summary>
    public uint Flags;

    /// <summary>
    /// Frames
    /// </summary>
    public byte Frames;

    /// <summary>
    /// Seconds
    /// </summary>
    public byte Seconds;

    /// <summary>
    /// Minutes
    /// </summary>
    public byte Minutes;

    /// <summary>
    /// Hours
    /// </summary>
    public byte Hours;

    /// <summary>
    /// User bits
    /// </summary>
    public fixed byte Userbits[4];
}