namespace SharpVideo.Linux.Native;

/// <summary>
/// DRM capabilities that can be queried via drmGetCap.
/// </summary>
public enum DrmCapability : ulong
{
    /// <summary>
    /// * If set to 1, the driver supports creating dumb buffers via the &DRM_IOCTL_MODE_CREATE_DUMB ioctl.
    /// </summary>
    DRM_CAP_DUMB_BUFFER = 0x1,

    /// <summary>
    /// If set to 1, the kernel supports specifying a :ref:`CRTC index<crtc_index>` in the high bits of &drm_wait_vblank_request.type.
    ///
    /// Starting kernel version 2.6.39, this capability is always set to 1.
    /// </summary>
    DRM_CAP_VBLANK_HIGH_CRTC = 0x2,

    /// <summary>
    /// The preferred bit depth for dumb buffers.
    ///
    /// The bit depth is the number of bits used to indicate the color of a single pixel excluding any padding.
    /// This is different from the number of bits per pixel.
    /// For instance, XRGB8888 has a bit depth of 24 but has 32 bits per pixel.
    ///
    /// Note that this preference only applies to dumb buffers, it's irrelevant for other types of buffers.
    /// </summary>
    DRM_CAP_DUMB_PREFERRED_DEPTH = 0x3,

    /// <summary>
    /// If set to 1, the driver prefers userspace to render to a shadow buffer instead of directly rendering to a dumb buffer.
    /// For best speed, userspace should do streaming ordered memory copies into the dumb buffer and never read from it.
    ///
    /// Note that this preference only applies to dumb buffers, it's irrelevant for other types of buffers.
    /// </summary>
    DRM_CAP_DUMB_PREFER_SHADOW = 0x4,

    /// <summary>
    /// Bitfield of supported PRIME sharing capabilities.
    /// See &DRM_PRIME_CAP_IMPORT and &DRM_PRIME_CAP_EXPORT.
    ///
    /// Starting from kernel version 6.6, both &DRM_PRIME_CAP_IMPORT and &DRM_PRIME_CAP_EXPORT are always advertised.
    ///
    /// PRIME buffers are exposed as dma-buf file descriptors.
    /// See :ref:`prime_buffer_sharing`.
    /// </summary>
    DRM_CAP_PRIME = 0x5,

    /// <summary>
    /// If set to 0, the kernel will report timestamps with ``CLOCK_REALTIME`` in struct drm_event_vblank.
    /// If set to 1, the kernel will report timestamps with ``CLOCK_MONOTONIC``.
    /// See ``clock_gettime(2)`` for the definition of these clocks.
    /// Starting from kernel version 2.6.39, the default value for this capability is 1.
    /// Starting kernel version 4.15, this capability is always set to 1.
    /// </summary>
    DRM_CAP_TIMESTAMP_MONOTONIC = 0x6,

    /// <summary>
    /// If set to 1, the driver supports &DRM_MODE_PAGE_FLIP_ASYNC for legacy page-flips.
    /// </summary>
    DRM_CAP_ASYNC_PAGE_FLIP = 0x7,

    /// <summary>
    /// The ``CURSOR_WIDTH`` and ``CURSOR_HEIGHT`` capabilities return a valid width x height combination for the hardware cursor.
    /// The intention is that a hardware agnostic userspace can query a cursor plane size to use.
    ///
    /// Note that the cross-driver contract is to merely return a valid size;
    /// drivers are free to attach another meaning on top, eg. i915 returns the maximum plane size.
    /// </summary>
    DRM_CAP_CURSOR_WIDTH = 0x8,

    /// <summary>
    /// See &DRM_CAP_CURSOR_WIDTH.
    /// </summary>
    DRM_CAP_CURSOR_HEIGHT = 0x9,

    /// <summary>
    /// If set to 1, the driver supports supplying modifiers in the &DRM_IOCTL_MODE_ADDFB2 ioctl.
    /// </summary>
    DRM_CAP_ADDFB2_MODIFIERS = 0x10,

    /// <summary>
    /// If set to 1, the driver supports the &DRM_MODE_PAGE_FLIP_TARGET_ABSOLUTE and &DRM_MODE_PAGE_FLIP_TARGET_RELATIVE flags in &drm_mode_crtc_page_flip_target.flags for the &DRM_IOCTL_MODE_PAGE_FLIP ioctl.
    /// </summary>
    DRM_CAP_PAGE_FLIP_TARGET = 0x11,

    /// <summary>
    /// If set to 1, the kernel supports reporting the CRTC ID in &drm_event_vblank.crtc_id for the &DRM_EVENT_VBLANK and &DRM_EVENT_FLIP_COMPLETE events.
    /// Starting kernel version 4.12, this capability is always set to 1.
    /// </summary>
    DRM_CAP_CRTC_IN_VBLANK_EVENT = 0x12,

    /// <summary>
    /// If set to 1, the driver supports sync objects. See :ref:`drm_sync_objects`.
    /// </summary>
    DRM_CAP_SYNCOBJ = 0x13,

    /// <summary>
    /// If set to 1, the driver supports timeline operations on sync objects. See :ref:`drm_sync_objects`.
    /// </summary>
    DRM_CAP_SYNCOBJ_TIMELINE = 0x14,

    /// <summary>
    /// If set to 1, the driver supports &DRM_MODE_PAGE_FLIP_ASYNC for atomic commits.
    /// </summary>
    DRM_CAP_ATOMIC_ASYNC_PAGE_FLIP = 0x15
}