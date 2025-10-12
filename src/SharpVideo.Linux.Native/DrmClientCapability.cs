namespace SharpVideo.Linux.Native;

/// <summary>
/// DRM client capabilities that can be enabled via drmSetClientCap.
/// </summary>
public enum DrmClientCapability : UInt64
{
    /// <summary>
    /// If set to 1, the DRM core will expose the stereo 3D capabilities of the
    /// monitor by advertising the supported 3D layouts in the flags of struct
    /// drm_mode_modeinfo.See ``DRM_MODE_FLAG_3D_*``.
    ///
    /// This capability is always supported for all drivers starting from kernel version 3.13.
    /// </summary>
    DRM_CLIENT_CAP_STEREO_3D = 1,

    /// <summary>
    /// If set to 1, the DRM core will expose all planes (overlay, primary, and cursor) to userspace.
    ///
    /// This capability has been introduced in kernel version 3.15. Starting from kernel version 3.17, this capability is always supported for all drivers.
    /// </summary>
    DRM_CLIENT_CAP_UNIVERSAL_PLANES = 2,

    /// <summary>
    /// If set to 1, the DRM core will expose atomic properties to userspace.
    /// This implicitly enables &DRM_CLIENT_CAP_UNIVERSAL_PLANES and &DRM_CLIENT_CAP_ASPECT_RATIO.
    /// If the driver doesn't support atomic mode-setting, enabling this capability will fail with -EOPNOTSUPP.
    ///
    /// This capability has been introduced in kernel version 4.0.
    /// Starting from kernel version 4.2, this capability is always supported for atomic-capable drivers.
    /// </summary>
    DRM_CLIENT_CAP_ATOMIC = 3,

    /// <summary>
    /// If set to 1, the DRM core will provide aspect ratio information in modes.
    /// See ``DRM_MODE_FLAG_PIC_AR_*``.
    ///
    /// This capability is always supported for all drivers starting from kernel version 4.18.
    /// </summary>
    DRM_CLIENT_CAP_ASPECT_RATIO = 4,

    /// <summary>
    /// If set to 1, the DRM core will expose special connectors to be used for writing back to memory the scene setup in the commit.
    /// The client must enable &DRM_CLIENT_CAP_ATOMIC first.
    ///
    /// This capability is always supported for atomic-capable drivers starting from kernel version 4.19.
    /// </summary>
    DRM_CLIENT_CAP_WRITEBACK_CONNECTORS = 5,

    /// <summary>
    /// Drivers for para-virtualized hardware (e.g. vmwgfx, qxl, virtio and virtualbox) have additional restrictions for cursor planes (thus making cursor planes on those drivers not truly universal,) e.g.
    /// they need cursor planes to act like one would expect from a mouse cursor and have correctly set hotspot properties.
    /// If this client cap is not set the DRM core will hide cursor plane on those virtualized drivers because not setting it implies that the client is not capable of dealing with those extra restictions.
    /// Clients which do set cursor hotspot and treat the cursor plane like a mouse cursor should set this property.
    /// The client must enable &DRM_CLIENT_CAP_ATOMIC first.
    ///
    /// Setting this property on drivers which do not special case cursor planes (i.e. non-virtualized drivers) will return EOPNOTSUPP, which can be used by userspace to gauge* requirements of the hardware/drivers they're running on.
    ///
    /// This capability is always supported for atomic-capable virtualized drivers starting from kernel version 6.6.
    /// </summary>
    DRM_CLIENT_CAP_CURSOR_PLANE_HOTSPOT = 6
}
