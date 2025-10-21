using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// DRM event context for handling page flip and vblank events.
/// Must match the layout of drmEventContext from libdrm.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DrmEventContext
{
    /// <summary>
    /// Version of this structure (should be DRM_EVENT_CONTEXT_VERSION).
    /// </summary>
    public int version;

    /// <summary>
    /// Callback for vblank events.
    /// </summary>
    public nint vblank_handler; // void (*vblank_handler)(...)

    /// <summary>
    /// Callback for page flip events.
    /// </summary>
    public nint page_flip_handler; // void (*page_flip_handler)(...)

    /// <summary>
    /// Callback for page flip events (version 2).
    /// </summary>
    public nint page_flip_handler2; // void (*page_flip_handler2)(...)

    /// <summary>
    /// Callback for sequence events.
    /// </summary>
    public nint sequence_handler; // void (*sequence_handler)(...)
}
