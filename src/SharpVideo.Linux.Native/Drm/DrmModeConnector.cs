using System.Runtime.InteropServices;

using SharpVideo.Linux.Native.Drm;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drmModeConnector</c> structure.
/// Contains connector information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct DrmModeConnector
{
    /// <summary>
    /// Connector ID.
    /// </summary>
    public readonly uint ConnectorId;

    /// <summary>
    /// Current encoder ID.
    /// </summary>
    public readonly uint EncoderId;

    /// <summary>
    /// Connector type.
    /// </summary>
    public readonly DrmConnectorType ConnectorType;

    /// <summary>
    /// Connector type ID.
    /// </summary>
    public readonly uint ConnectorTypeId;

    /// <summary>
    /// Connection status.
    /// </summary>
    public readonly DrmModeConnection Connection;

    /// <summary>
    /// Physical width in millimeters.
    /// </summary>
    public readonly uint MmWidth;

    /// <summary>
    /// Physical height in millimeters.
    /// </summary>
    public readonly uint MmHeight;

    /// <summary>
    /// Subpixel layout.
    /// </summary>
    public readonly DrmModeSubPixel SubPixel;

    /// <summary>
    /// Number of modes.
    /// </summary>
    public readonly int CountModes;

    /// <summary>
    /// Pointer to modes array.
    /// </summary>
    private readonly DrmModeModeInfo* _modes;

    /// <summary>
    /// Number of properties.
    /// </summary>
    public readonly int CountProps;

    /// <summary>
    /// Pointer to property IDs array.
    /// </summary>
    private readonly uint* _props;

    /// <summary>
    /// Pointer to property values array.
    /// </summary>
    private readonly ulong* _propValues;

    /// <summary>
    /// Number of encoders.
    /// </summary>
    public readonly int CountEncoders;

    /// <summary>
    /// Pointer to encoder IDs array.
    /// </summary>
    private readonly uint* _encoders;

    /// <summary>
    /// Gets a span over the available modes.
    /// </summary>
    public ReadOnlySpan<DrmModeModeInfo> Modes => _modes == null
        ? ReadOnlySpan<DrmModeModeInfo>.Empty
        : new ReadOnlySpan<DrmModeModeInfo>(_modes, CountModes);

    /// <summary>
    /// Gets a span over the property IDs.
    /// </summary>
    public ReadOnlySpan<uint> Props => _props == null
        ? ReadOnlySpan<uint>.Empty
        : new ReadOnlySpan<uint>(_props, CountProps);

    /// <summary>
    /// Gets a span over the property values.
    /// </summary>
    public ReadOnlySpan<ulong> PropValues => _propValues == null
        ? ReadOnlySpan<ulong>.Empty
        : new ReadOnlySpan<ulong>(_propValues, CountProps);

    /// <summary>
    /// Gets a span over the encoder IDs.
    /// </summary>
    public ReadOnlySpan<uint> Encoders => _encoders == null
        ? ReadOnlySpan<uint>.Empty
        : new ReadOnlySpan<uint>(_encoders, CountEncoders);
}