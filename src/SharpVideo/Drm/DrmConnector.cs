using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.Drm;

namespace SharpVideo.Drm;

public class DrmConnector
{
    /// <summary>
    /// Connector ID.
    /// </summary>
    public required uint ConnectorId { get; init; }

    /// <summary>
    /// Current encoder ID.
    /// </summary>
    public required DrmEncoder? Encoder { get; init; }

    /// <summary>
    /// Connector type.
    /// </summary>
    public required DrmConnectorType ConnectorType { get; init; }

    /// <summary>
    /// Connector type ID.
    /// </summary>
    public required uint ConnectorTypeId { get; init; }

    /// <summary>
    /// Connection status.
    /// </summary>
    public required DrmModeConnection Connection { get; init; }

    /// <summary>
    /// Physical width in millimeters.
    /// </summary>
    public required uint MmWidth { get; init; }

    /// <summary>
    /// Physical height in millimeters.
    /// </summary>
    public required uint MmHeight { get; init; }

    /// <summary>
    /// Subpixel layout.
    /// </summary>
    public required DrmModeSubPixel SubPixel { get; init; }

    public required IReadOnlyCollection<DrmModeInfo> Modes { get; init; }

    public required IReadOnlyCollection<DrmProperty> Props { get; init; }

    public required IReadOnlyCollection<DrmEncoder> Encoders { get; init; }
}