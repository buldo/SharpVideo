namespace SharpVideo.Linux.Native;

/// <summary>
/// DRM client capabilities that can be enabled via drmSetClientCap.
/// </summary>
public enum DrmClientCapability : ulong
{
    /// <summary>
    /// Stereo 3D support.
    /// </summary>
    Stereo3D = 1,

    /// <summary>
    /// Universal planes capability - exposes all planes including primary planes.
    /// When enabled, all planes (primary, overlay, cursor) are visible through the plane API.
    /// </summary>
    UniversalPlanes = 2,

    /// <summary>
    /// Atomic modesetting capability.
    /// Enables atomic operations for display configuration changes.
    /// Implicitly enables UniversalPlanes and AspectRatio.
    /// </summary>
    Atomic = 3,

    /// <summary>
    /// Aspect ratio capability.
    /// Enables aspect ratio information in mode structures.
    /// </summary>
    AspectRatio = 4,

    /// <summary>
    /// Writeback connectors capability.
    /// Enables writeback connector support for capturing display output.
    /// Requires Atomic capability to be enabled first.
    /// </summary>
    WritebackConnectors = 5,

    /// <summary>
    /// Cursor plane hotspot capability.
    /// Enables cursor plane hotspot positioning.
    /// Requires Atomic capability to be enabled first.
    /// </summary>
    CursorPlaneHotspot = 6
}
