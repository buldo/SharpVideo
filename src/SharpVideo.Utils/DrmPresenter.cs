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
#if DEBUG
        DumpCapabilities(capabilities, logger);
#endif

        AtomicDisplayManager? atomicDisplayManager = null;
        if (!capabilities.AtomicAsyncPageFlip)
        {
            logger.LogInformation(
                "Using atomic modesetting with VBlank synchronization (async page flip not supported)");

            var props = new AtomicPlaneProperties(overlayPlane);

            if (props.IsValid())
            {
                logger.LogError("Failed to find required plane properties");
                return null;
            }

            atomicDisplayManager = new AtomicDisplayManager(
                drmDevice.DeviceFd,
                overlayPlane.Id,
                crtcId,
                props,
                width,
                height,
                width,
                height,
                logger);
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
            logger);
        var primaryPlanePresenter = new DrmPlaneDoubleBufferPresenter(
            drmDevice,
            primaryPlane,
            crtcId,
            width,
            height,
            capabilities,
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
}