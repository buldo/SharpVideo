namespace SharpVideo.Linux.Native;

/// <summary>
/// DRM capabilities that can be queried via drmGetCap.
/// </summary>
public enum DrmCapability : ulong
{
    /// <summary>
    /// Dumb buffer support - simple non-accelerated buffers.
    /// </summary>
    DumbBuffer = 0x1,

    /// <summary>
    /// Support for high CRTC numbers in vblank events.
    /// </summary>
    VblankHighCrtc = 0x2,

    /// <summary>
    /// Preferred depth for dumb buffers.
    /// </summary>
    DumbPreferredDepth = 0x3,

    /// <summary>
    /// Whether dumb buffers should be shadowed.
    /// </summary>
    DumbPreferShadow = 0x4,

    /// <summary>
    /// PRIME buffer sharing capabilities (DMA-BUF import/export).
    /// </summary>
    Prime = 0x5,

    /// <summary>
    /// Whether timestamps are monotonic.
    /// </summary>
    TimestampMonotonic = 0x6,

    /// <summary>
    /// Async page flip support.
    /// </summary>
    AsyncPageFlip = 0x7,

    /// <summary>
    /// Maximum cursor width.
    /// </summary>
    CursorWidth = 0x8,

    /// <summary>
    /// Maximum cursor height.
    /// </summary>
    CursorHeight = 0x9,

    /// <summary>
    /// Support for format modifiers in AddFB2.
    /// </summary>
    AddFB2Modifiers = 0x10,

    /// <summary>
    /// Page flip target support.
    /// </summary>
    PageFlipTarget = 0x11,

    /// <summary>
    /// CRTC ID in vblank events.
    /// </summary>
    CrtcInVblankEvent = 0x12,

    /// <summary>
    /// Sync object support.
    /// </summary>
    SyncObj = 0x13,

    /// <summary>
    /// Timeline sync object support.
    /// </summary>
    SyncObjTimeline = 0x14,

    /// <summary>
    /// Atomic async page flip support.
    /// </summary>
    AtomicAsyncPageFlip = 0x15
}
