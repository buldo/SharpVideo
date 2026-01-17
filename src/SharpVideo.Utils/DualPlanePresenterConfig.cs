using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Drm;
using SharpVideo.Linux.Native;

namespace SharpVideo.Utils;

/// <summary>
/// Configuration for plane drawing dimensions and position.
/// </summary>
/// <param name="SrcWidth">Source width (framebuffer/content width).</param>
/// <param name="SrcHeight">Source height (framebuffer/content height).</param>
/// <param name="DstX">Destination X position on CRTC.</param>
/// <param name="DstY">Destination Y position on CRTC.</param>
/// <param name="DstWidth">Destination width on CRTC (0 = use SrcWidth).</param>
/// <param name="DstHeight">Destination height on CRTC (0 = use SrcHeight).</param>
public readonly record struct PlaneDrawConfiguration(
    uint SrcWidth,
    uint SrcHeight,
    uint DstX = 0,
    uint DstY = 0,
    uint DstWidth = 0,
    uint DstHeight = 0)
{
    /// <summary>
    /// Gets the effective destination width.
    /// </summary>
    public uint EffectiveDstWidth => DstWidth > 0 ? DstWidth : SrcWidth;

    /// <summary>
    /// Gets the effective destination height.
    /// </summary>
    public uint EffectiveDstHeight => DstHeight > 0 ? DstHeight : SrcHeight;
}

/// <summary>
/// Immutable configuration for the DualPlanePresenter2.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed record DualPlanePresenterConfig
{
    /// <summary>
    /// Whether the video plane is enabled for this presenter.
    /// </summary>
    public required bool VideoPlaneEnabled { get; init; }

    /// <summary>
    /// Whether the OSD plane is enabled for this presenter.
    /// </summary>
    public required bool OsdPlaneEnabled { get; init; }

    /// <summary>
    /// The video plane (required if VideoPlaneEnabled is true).
    /// </summary>
    public DrmPlane? VideoPlane { get; init; }

    /// <summary>
    /// The OSD plane (required if OsdPlaneEnabled is true).
    /// </summary>
    public DrmPlane? OsdPlane { get; init; }

    /// <summary>
    /// The CRTC ID for modesetting.
    /// </summary>
    public required uint CrtcId { get; init; }

    /// <summary>
    /// The connector ID for modesetting.
    /// </summary>
    public required uint ConnectorId { get; init; }

    /// <summary>
    /// The display mode to use.
    /// </summary>
    public required DrmModeModeInfo Mode { get; init; }

    /// <summary>
    /// Drawing configuration for the video plane (dimensions and position).
    /// Required if VideoPlaneEnabled is true.
    /// </summary>
    public PlaneDrawConfiguration? VideoDrawConfig { get; init; }

    /// <summary>
    /// Drawing configuration for the OSD plane (dimensions and position).
    /// Required if OsdPlaneEnabled is true.
    /// </summary>
    public PlaneDrawConfiguration? OsdDrawConfig { get; init; }

    /// <summary>
    /// Z-position assignment for layer ordering.
    /// Can be null if hardware defaults are acceptable or zpos is not supported.
    /// </summary>
    public ZPosAssignment? ZPos { get; init; }

    /// <summary>
    /// Optional logger for diagnostics.
    /// </summary>
    public ILogger? Logger { get; init; }

    /// <summary>
    /// Validates the configuration and throws if invalid.
    /// </summary>
    /// <exception cref="InvalidOperationException">If the configuration is invalid.</exception>
    public void Validate()
    {
        if (!VideoPlaneEnabled && !OsdPlaneEnabled)
        {
            throw new InvalidOperationException(
                "At least one plane must be enabled (VideoPlaneEnabled or OsdPlaneEnabled).");
        }

        if (VideoPlaneEnabled)
        {
            if (VideoPlane == null)
            {
                throw new InvalidOperationException(
                    "VideoPlane must be specified when VideoPlaneEnabled is true.");
            }

            if (!VideoDrawConfig.HasValue)
            {
                throw new InvalidOperationException(
                    "VideoDrawConfig must be specified when VideoPlaneEnabled is true.");
            }

            if (VideoDrawConfig.Value.SrcWidth == 0 || VideoDrawConfig.Value.SrcHeight == 0)
            {
                throw new InvalidOperationException(
                    "VideoDrawConfig must have non-zero SrcWidth and SrcHeight.");
            }
        }

        if (OsdPlaneEnabled)
        {
            if (OsdPlane == null)
            {
                throw new InvalidOperationException(
                    "OsdPlane must be specified when OsdPlaneEnabled is true.");
            }

            if (!OsdDrawConfig.HasValue)
            {
                throw new InvalidOperationException(
                    "OsdDrawConfig must be specified when OsdPlaneEnabled is true.");
            }

            if (OsdDrawConfig.Value.SrcWidth == 0 || OsdDrawConfig.Value.SrcHeight == 0)
            {
                throw new InvalidOperationException(
                    "OsdDrawConfig must have non-zero SrcWidth and SrcHeight.");
            }
        }

        if (VideoPlaneEnabled && OsdPlaneEnabled && VideoPlane!.Id == OsdPlane!.Id)
        {
            throw new InvalidOperationException(
                "VideoPlane and OsdPlane must be different planes.");
        }

        if (CrtcId == 0)
        {
            throw new InvalidOperationException("CrtcId must be non-zero.");
        }

        if (ConnectorId == 0)
        {
            throw new InvalidOperationException("ConnectorId must be non-zero.");
        }
    }

    /// <summary>
    /// Creates a validated configuration builder for convenience.
    /// </summary>
    public static DualPlanePresenterConfigBuilder CreateBuilder() => new();
}

/// <summary>
/// Builder for creating DualPlanePresenterConfig instances.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class DualPlanePresenterConfigBuilder
{
    private bool _videoPlaneEnabled;
    private bool _osdPlaneEnabled;
    private DrmPlane? _videoPlane;
    private DrmPlane? _osdPlane;
    private uint _crtcId;
    private uint _connectorId;
    private DrmModeModeInfo _mode;
    private PlaneDrawConfiguration? _videoDrawConfig;
    private PlaneDrawConfiguration? _osdDrawConfig;
    private ZPosAssignment? _zPos;
    private ILogger? _logger;

    public DualPlanePresenterConfigBuilder WithVideoPlane(DrmPlane plane, PlaneDrawConfiguration drawConfig)
    {
        _videoPlaneEnabled = true;
        _videoPlane = plane;
        _videoDrawConfig = drawConfig;
        return this;
    }

    public DualPlanePresenterConfigBuilder WithOsdPlane(DrmPlane plane, PlaneDrawConfiguration drawConfig)
    {
        _osdPlaneEnabled = true;
        _osdPlane = plane;
        _osdDrawConfig = drawConfig;
        return this;
    }

    public DualPlanePresenterConfigBuilder WithCrtc(uint crtcId)
    {
        _crtcId = crtcId;
        return this;
    }

    public DualPlanePresenterConfigBuilder WithConnector(uint connectorId)
    {
        _connectorId = connectorId;
        return this;
    }

    public DualPlanePresenterConfigBuilder WithMode(DrmModeModeInfo mode)
    {
        _mode = mode;
        return this;
    }

    public DualPlanePresenterConfigBuilder WithZPos(ZPosAssignment zPos)
    {
        _zPos = zPos;
        return this;
    }

    public DualPlanePresenterConfigBuilder WithLogger(ILogger? logger)
    {
        _logger = logger;
        return this;
    }

    public DualPlanePresenterConfig Build()
    {
        var config = new DualPlanePresenterConfig
        {
            VideoPlaneEnabled = _videoPlaneEnabled,
            OsdPlaneEnabled = _osdPlaneEnabled,
            VideoPlane = _videoPlane,
            OsdPlane = _osdPlane,
            CrtcId = _crtcId,
            ConnectorId = _connectorId,
            Mode = _mode,
            VideoDrawConfig = _videoDrawConfig,
            OsdDrawConfig = _osdDrawConfig,
            ZPos = _zPos,
            Logger = _logger
        };

        config.Validate();
        return config;
    }
}
