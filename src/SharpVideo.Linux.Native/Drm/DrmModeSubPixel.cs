namespace SharpVideo.Linux.Native;

/// <summary>
/// DRM connector subpixel layout.
/// </summary>
public enum DrmModeSubPixel : uint
{
    /// <summary>
    /// Subpixel layout is unknown.
    /// </summary>
    Unknown = 1,

    /// <summary>
    /// Horizontal RGB subpixel layout.
    /// </summary>
    HorizontalRgb = 2,

    /// <summary>
    /// Horizontal BGR subpixel layout.
    /// </summary>
    HorizontalBgr = 3,

    /// <summary>
    /// Vertical RGB subpixel layout.
    /// </summary>
    VerticalRgb = 4,

    /// <summary>
    /// Vertical BGR subpixel layout.
    /// </summary>
    VerticalBgr = 5,

    /// <summary>
    /// No subpixel layout.
    /// </summary>
    None = 6
}