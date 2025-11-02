namespace SharpVideo.Linux.Native;

/// <summary>
/// DRM connector connection status.
/// </summary>
public enum DrmModeConnection : uint
{
    /// <summary>
    /// Connector is connected to a display device.
    /// </summary>
    Connected = 1,

    /// <summary>
    /// Connector is not connected to any display device.
    /// </summary>
    Disconnected = 2,

    /// <summary>
    /// Connection status is unknown.
    /// </summary>
    Unknown = 3
}