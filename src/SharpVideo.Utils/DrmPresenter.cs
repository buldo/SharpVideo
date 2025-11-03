using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;

namespace SharpVideo.Utils;

[SupportedOSPlatform("linux")]
public class DrmPresenter
{
    private readonly ILogger _logger;

    private DrmPresenter(
        DrmPlane primaryPlane,
        DrmPlaneDoubleBufferPresenter primaryPlanePresenter,
        DrmPlane overlayPlane,
        DrmPlaneLastDmaBufferPresenter overlayPlanePresenter,
        ILogger logger)
    {
        PrimaryPlane = primaryPlane;
        PrimaryPlanePresenter = primaryPlanePresenter;
        OverlayPlane = overlayPlane;
        OverlayPlanePresenter = overlayPlanePresenter;
        _logger = logger;
    }

    public DrmPlane PrimaryPlane { get; }

    public DrmPlaneDoubleBufferPresenter PrimaryPlanePresenter { get; }

    public DrmPlane OverlayPlane { get; }

    public DrmPlaneLastDmaBufferPresenter OverlayPlanePresenter { get; }

    public static DrmPresenter? Create(
        DrmDevice drmDevice,
        uint width,
        uint height,
        DrmBufferManager bufferManager,
        PixelFormat primaryPlanePixelFormat,
        PixelFormat overlayPlanePixelFormat,
        ILogger logger)
    {
        var resources = drmDevice.GetResources();
        if (resources == null)
        {
            throw new Exception("Failed to get DRM resources");
        }

        var connector = resources.Connectors.FirstOrDefault(c => c.Connection == DrmModeConnection.Connected);
        if (connector == null)
        {
            throw new Exception("No connected display found");
        }

        logger.LogInformation("Found connected display: {Type}", connector.ConnectorType);

        var mode = connector.Modes.FirstOrDefault(m => m.HDisplay == width && m.VDisplay == height);
        if (mode == null)
        {
            throw new Exception($"No {width}x{height} mode found");
        }

        logger.LogInformation("Using mode: {Name} ({Width}x{Height}@{RefreshRate}Hz)",
            mode.Name, mode.HDisplay, mode.VDisplay, mode.VRefresh);

        var encoder = connector.Encoder ?? connector.Encoders.FirstOrDefault();
        if (encoder == null)
        {
            throw new Exception("No encoder found");
        }

        var crtcId = encoder.CrtcId;
        if (crtcId == 0)
        {
            var availableCrtcs = resources.Crtcs
                .Where(crtc => (encoder.PossibleCrtcs & (1u << Array.IndexOf(resources.Crtcs.ToArray(), crtc))) != 0);
            crtcId = availableCrtcs.FirstOrDefault();
        }

        if (crtcId == 0)
        {
            throw new Exception("No available CRTC found");
        }

        logger.LogInformation("Using CRTC ID: {CrtcId}", crtcId);

        var crtcIndex = resources.Crtcs.ToList().IndexOf(crtcId);
        var compatiblePlanes = resources.Planes
            .Where(p => (p.PossibleCrtcs & (1u << crtcIndex)) != 0)
            .ToList();

        var primaryPlane = compatiblePlanes.FirstOrDefault(p =>
        {
            var props = p.GetProperties();
            var typeProp = props.FirstOrDefault(prop => prop.Name.Equals("type", StringComparison.OrdinalIgnoreCase));
            return typeProp != null && typeProp.EnumNames != null &&
                   typeProp.Value < (ulong)typeProp.EnumNames.Count &&
                   typeProp.EnumNames[(int)typeProp.Value].Equals("Primary", StringComparison.OrdinalIgnoreCase);
        });

        if (primaryPlane == null)
        {
            throw new Exception("No primary plane found");
        }

        logger.LogInformation("Found primary plane: ID {PlaneId}", primaryPlane.Id);

        var overlayPlane = compatiblePlanes.FirstOrDefault(p =>
        {
            var props = p.GetProperties();
            var typeProp = props.FirstOrDefault(prop => prop.Name.Equals("type", StringComparison.OrdinalIgnoreCase));
            bool isOverlay = typeProp != null && typeProp.EnumNames != null &&
                             typeProp.Value < (ulong)typeProp.EnumNames.Count &&
                             typeProp.EnumNames[(int)typeProp.Value]
                                 .Equals("Overlay", StringComparison.OrdinalIgnoreCase);
            return isOverlay && p.Formats.Contains(overlayPlanePixelFormat.Fourcc);
        });

        if (overlayPlane == null)
        {
            throw new Exception($"No overlay plane with {overlayPlanePixelFormat.GetName()} format found");
        }

        logger.LogInformation("Found {Format} overlay plane: ID {PlaneId}",
            overlayPlanePixelFormat.GetName(), overlayPlane.Id);

        var capabilities = drmDevice.GetDeviceCapabilities();
        AtomicPlaneUpdater? atomicUpdater = null;
        AtomicDisplayManager? atomicDisplayManager = null;
#if DEBUG
        DumpCapabilities(capabilities, logger);
#endif

        if (!capabilities.AtomicAsyncPageFlip)
        {
            logger.LogInformation(
                "Using atomic modesetting with VBlank synchronization (async page flip not supported)");

            var fbIdPropertyId = overlayPlane.GetPlanePropertyId("FB_ID");
            var crtcIdPropertyId = overlayPlane.GetPlanePropertyId("CRTC_ID");
            var crtcXPropertyId = overlayPlane.GetPlanePropertyId("CRTC_X");
            var crtcYPropertyId = overlayPlane.GetPlanePropertyId("CRTC_Y");
            var crtcWPropertyId = overlayPlane.GetPlanePropertyId("CRTC_W");
            var crtcHPropertyId = overlayPlane.GetPlanePropertyId("CRTC_H");
            var srcXPropertyId = overlayPlane.GetPlanePropertyId("SRC_X");
            var srcYPropertyId = overlayPlane.GetPlanePropertyId("SRC_Y");
            var srcWPropertyId = overlayPlane.GetPlanePropertyId("SRC_W");
            var srcHPropertyId = overlayPlane.GetPlanePropertyId("SRC_H");

            if (fbIdPropertyId == 0 || crtcIdPropertyId == 0 ||
                crtcXPropertyId == 0 || crtcYPropertyId == 0 ||
                crtcWPropertyId == 0 || crtcHPropertyId == 0 ||
                srcXPropertyId == 0 || srcYPropertyId == 0 ||
                srcWPropertyId == 0 || srcHPropertyId == 0)
            {
                logger.LogError("Failed to find required plane properties");
                return null;
            }

            atomicDisplayManager = new AtomicDisplayManager(
                drmDevice.DeviceFd,
                overlayPlane.Id,
                crtcId,
                fbIdPropertyId,
                crtcIdPropertyId,
                crtcXPropertyId,
                crtcYPropertyId,
                crtcWPropertyId,
                crtcHPropertyId,
                srcXPropertyId,
                srcYPropertyId,
                srcWPropertyId,
                srcHPropertyId,
                width,
                height,
                width,
                height,
                logger);
        }
        else
        {
            atomicUpdater = new AtomicPlaneUpdater(drmDevice.DeviceFd, overlayPlane.Id, crtcId);
            logger.LogInformation("Atomic modesetting with async page flip initialized");
        }

        // Create double buffers for primary plane
        logger.LogInformation("Creating double buffers for primary plane with {Format} format",
            primaryPlanePixelFormat.GetName());

        var overlayPlanePresenter = new DrmPlaneLastDmaBufferPresenter(
            drmDevice,
            overlayPlane,
            crtcId,
            width,
            height,
            capabilities,
            bufferManager,
            atomicDisplayManager,
            atomicUpdater,
            logger);
        var primaryPlanePresenter = new DrmPlaneDoubleBufferPresenter(
            drmDevice,
            primaryPlane,
            crtcId,
            width,
            height,
            capabilities,
            atomicUpdater,
            logger,
            bufferManager,
            primaryPlanePixelFormat,
            connector.ConnectorId,
            mode);
        return new DrmPresenter(
            primaryPlane,
            primaryPlanePresenter,
            overlayPlane,
            overlayPlanePresenter,
            logger);
    }

    private static void DumpCapabilities(DrmCapabilitiesState caps, ILogger logger)
    {
        logger.LogInformation(
            "DRM device capabilities: " +
            "DumbBuffer: {DumbBuffer}; " +
            "VblankHighCrtc: {VblankHighCrtc}; " +
            "DumbPreferredDepth: {DumbPreferredDepth}; " +
            "DumbPreferShadow: {DumbPreferShadow}; " +
            "Prime: {Prime}; " +
            "TimestampMonotonic: {TimestampMonotonic}; " +
            "AsyncPageFlip: {AsyncPageFlip}; " +
            "CursorWidth: {CursorWidth}; " +
            "CursorHeight: {CursorHeight}; " +
            "AddFB2Modifiers: {AddFB2Modifiers}; " +
            "PageFlipTarget: {PageFlipTarget}; " +
            "CrtcInVblankEvent: {CrtcInVblankEvent}; " +
            "SyncObj: {SyncObj}; " +
            "SyncObjTimeline: {SyncObjTimeline}; " +
            "AtomicAsyncPageFlip: {AtomicAsyncPageFlip}",
            caps.DumbBuffer,
            caps.VblankHighCrtc,
            caps.DumbPreferredDepth,
            caps.DumbPreferShadow,
            caps.Prime,
            caps.TimestampMonotonic,
            caps.AsyncPageFlip,
            caps.CursorWidth,
            caps.CursorHeight,
            caps.AddFB2Modifiers,
            caps.PageFlipTarget,
            caps.CrtcInVblankEvent,
            caps.SyncObj,
            caps.SyncObjTimeline,
            caps.AtomicAsyncPageFlip);
    }

    public void CleanupDisplay()
    {
        _logger.LogInformation("Cleaning up display resources");

        try
        {
            PrimaryPlanePresenter.Cleanup();
            OverlayPlanePresenter.Cleanup();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during display cleanup");
        }
    }

    public void SetPrimaryPlaneOverOverlayPlane()
    {
        // Get zpos ranges for both planes
        var primaryZposRange = PrimaryPlane.GetPlaneZPositionRange();
        var overlayZposRange = OverlayPlane.GetPlaneZPositionRange();

        if (primaryZposRange.HasValue)
        {
            _logger.LogInformation("Primary plane zpos range: [{Min}, {Max}], current: {Current}",
                primaryZposRange.Value.min, primaryZposRange.Value.max, primaryZposRange.Value.current);
        }
        else
        {
            _logger.LogWarning("Primary plane does not support zpos property");
        }

        if (overlayZposRange.HasValue)
        {
            _logger.LogInformation("Overlay plane zpos range: [{Min}, {Max}], current: {Current}",
                overlayZposRange.Value.min, overlayZposRange.Value.max, overlayZposRange.Value.current);
        }
        else
        {
            _logger.LogWarning("Overlay plane does not support zpos property");
        }

        // Try to set z-position to make primary plane appear on top
        if (primaryZposRange.HasValue && overlayZposRange.HasValue)
        {
            var primaryZpos = primaryZposRange.Value.max;
            var overlayZpos = overlayZposRange.Value.min;

            _logger.LogInformation("Attempting to set Primary zpos={PrimaryZpos}, Overlay zpos={OverlayZpos}", primaryZpos, overlayZpos);

            var primarySuccess = PrimaryPlane.SetPlaneZPosition(primaryZpos);
            var overlaySuccess = OverlayPlane.SetPlaneZPosition(overlayZpos);

            if (primarySuccess && overlaySuccess)
            {
                _logger.LogInformation("Z-positioning successful: Primary on top, Overlay below");
            }
            else
            {
                _logger.LogWarning("Failed to set z-positions as desired");
            }
        }
    }
}