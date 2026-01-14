using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// V4L2 event subscription structure (v4l2_event_subscription).
/// Used with VIDIOC_SUBSCRIBE_EVENT and VIDIOC_UNSUBSCRIBE_EVENT ioctls.
/// </summary>
/// <remarks>
/// Size: 32 bytes on Linux.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
[SupportedOSPlatform("linux")]
public unsafe struct V4L2EventSubscription
{
    /// <summary>
    /// Event type to subscribe to (V4L2_EVENT_*).
    /// </summary>
    public uint Type;

    /// <summary>
    /// Event ID (control ID for V4L2_EVENT_CTRL).
    /// </summary>
    public uint Id;

    /// <summary>
    /// Event subscription flags (V4L2_EVENT_SUB_FL_*).
    /// </summary>
    public uint Flags;

    /// <summary>
    /// Reserved for future extensions.
    /// </summary>
    public fixed uint Reserved[5];
}
