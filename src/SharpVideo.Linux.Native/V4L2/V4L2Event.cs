using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// V4L2 event structure (v4l2_event).
/// Returned by VIDIOC_DQEVENT ioctl.
/// </summary>
/// <remarks>
/// Size: 136 bytes on Linux (64-bit).
/// The union 'u' contains different event-specific data based on Type.
/// Note: There is 4-byte padding after Type due to 8-byte alignment of the union.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
[SupportedOSPlatform("linux")]
public unsafe struct V4L2Event
{
    /// <summary>
    /// Event type (V4L2_EVENT_*).
    /// </summary>
    public uint Type;

    /// <summary>
    /// Padding for 8-byte alignment of the union.
    /// </summary>
    private uint _padding;

    /// <summary>
    /// Union of event-specific data (64 bytes).
    /// Interpretation depends on Type:
    /// - V4L2_EVENT_VSYNC: v4l2_event_vsync
    /// - V4L2_EVENT_CTRL: v4l2_event_ctrl
    /// - V4L2_EVENT_FRAME_SYNC: v4l2_event_frame_sync
    /// - V4L2_EVENT_SOURCE_CHANGE: v4l2_event_src_change (first 4 bytes contain 'changes' flags)
    /// - V4L2_EVENT_MOTION_DET: v4l2_event_motion_det
    /// </summary>
    public fixed byte U[64];

    /// <summary>
    /// Number of pending events for this type.
    /// </summary>
    public uint Pending;

    /// <summary>
    /// Event sequence number.
    /// </summary>
    public uint Sequence;

    /// <summary>
    /// Timestamp when the event occurred.
    /// </summary>
    public TimeSpec Timestamp;

    /// <summary>
    /// Event ID (control ID for V4L2_EVENT_CTRL).
    /// </summary>
    public uint Id;

    /// <summary>
    /// Reserved for future extensions.
    /// </summary>
    public fixed uint Reserved[8];

    /// <summary>
    /// Gets the source change flags from the union data.
    /// Only valid when Type is V4L2_EVENT_SOURCE_CHANGE.
    /// </summary>
    public uint SourceChangeFlags
    {
        get
        {
            fixed (byte* ptr = U)
            {
                return *(uint*)ptr;
            }
        }
    }
}

/// <summary>
/// Timespec structure matching Linux kernel's struct timespec.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[SupportedOSPlatform("linux")]
public struct TimeSpec
{
    /// <summary>
    /// Seconds.
    /// </summary>
    public long TvSec;

    /// <summary>
    /// Nanoseconds.
    /// </summary>
    public long TvNsec;
}
