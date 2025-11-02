namespace SharpVideo.Linux.Native;

/// <summary>
/// Opaque handle to a DRM atomic request.
/// Managed via drmModeAtomicAlloc, drmModeAtomicFree, etc.
/// </summary>
public struct DrmModeAtomicReq
{
    public nint Handle;
}

/// <summary>
/// Flags for drmModeAtomicCommit
/// </summary>
[Flags]
public enum DrmModeAtomicFlags : uint
{
    /// <summary>
    /// Test the configuration without applying it
    /// </summary>
    DRM_MODE_ATOMIC_TEST_ONLY = 0x0100,

    /// <summary>
    /// Apply the configuration without blocking
    /// </summary>
    DRM_MODE_ATOMIC_NONBLOCK = 0x0200,

    /// <summary>
    /// Allow the update to be incomplete (used for modeset)
    /// </summary>
    DRM_MODE_ATOMIC_ALLOW_MODESET = 0x0400,

    /// <summary>
    /// Request a page flip event
    /// </summary>
    DRM_MODE_PAGE_FLIP_EVENT = 0x01,

    /// <summary>
    /// Request an async page flip (if supported)
    /// </summary>
    DRM_MODE_PAGE_FLIP_ASYNC = 0x02,
}
