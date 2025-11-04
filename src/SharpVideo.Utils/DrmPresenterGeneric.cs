using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Linux.Native;

namespace SharpVideo.Utils;

/// <summary>
/// DRM presenter with configurable primary and overlay plane presenters.
/// Implements thread-safe disposal pattern for proper resource cleanup.
/// </summary>
/// <typeparam name="TPrimaryPresenter">Type of primary plane presenter</typeparam>
/// <typeparam name="TOverlayPresenter">Type of overlay plane presenter (use object if no overlay)</typeparam>
/// <remarks>
/// Thread Safety: 
/// - Factory methods are thread-safe
/// - Instance methods are NOT thread-safe - use from single thread
/// - Disposal is thread-safe and idempotent
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class DrmPresenter<TPrimaryPresenter, TOverlayPresenter> : IDisposable
    where TPrimaryPresenter : DrmSinglePlanePresenter
    where TOverlayPresenter : class
{
    private readonly ILogger _logger;
    private readonly int _drmDeviceFd;
    private bool _disposed;

    private DrmPresenter(
        DrmPlane primaryPlane,
        TPrimaryPresenter primaryPlanePresenter,
        DrmPlane? overlayPlane,
        TOverlayPresenter? overlayPlanePresenter,
        int drmDeviceFd,
        ILogger logger)
    {
        PrimaryPlane = primaryPlane;
        PrimaryPlanePresenter = primaryPlanePresenter;
        OverlayPlane = overlayPlane;
        OverlayPlanePresenter = overlayPlanePresenter;
        _drmDeviceFd = drmDeviceFd;
        _logger = logger;
    }

    public DrmPlane PrimaryPlane { get; }

    public TPrimaryPresenter PrimaryPlanePresenter { get; }

    public DrmPlane? OverlayPlane { get; }

    public TOverlayPresenter? OverlayPlanePresenter { get; }

    /// <summary>
    /// Creates a DRM presenter with double-buffered DMA primary plane and optional overlay.
    /// </summary>
    /// <exception cref="DrmResourceNotFoundException">Required DRM resources not found</exception>
    /// <exception cref="DrmPlaneNotFoundException">Required plane not found</exception>
    public static DrmPresenter<DrmPlaneDoubleBufferPresenter, DrmPlaneLastDmaBufferPresenter> CreateWithDmaBuffers(
        DrmDevice drmDevice,
        uint width,
        uint height,
        DrmBufferManager bufferManager,
        PixelFormat primaryPlanePixelFormat,
        PixelFormat? overlayPlanePixelFormat,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(drmDevice);
        ArgumentNullException.ThrowIfNull(bufferManager);
        ArgumentNullException.ThrowIfNull(logger);

        var resources = GetResourcesOrThrow(drmDevice, logger);
        var (primaryPlane, crtcId, connector, mode) = SetupPrimaryPlane(
            drmDevice, resources, width, height, logger);

        DrmPlane? overlayPlane = null;
        DrmPlaneLastDmaBufferPresenter? overlayPlanePresenter = null;

        if (overlayPlanePixelFormat != null)
        {
            overlayPlane = FindOverlayPlaneOrThrow(
                drmDevice, resources, crtcId, overlayPlanePixelFormat, logger);

            logger.LogInformation("Found {Format} overlay plane: ID {PlaneId}",
                overlayPlanePixelFormat.GetName(), overlayPlane.Id);

            overlayPlanePresenter = new DrmPlaneLastDmaBufferPresenter(
                drmDevice,
                overlayPlane,
                crtcId,
                width,
                height,
                bufferManager,
                logger);
        }

        var primaryPlanePresenter = new DrmPlaneDoubleBufferPresenter(
            drmDevice,
            primaryPlane,
            crtcId,
            width,
            height,
            logger,
            bufferManager,
            primaryPlanePixelFormat,
            connector.ConnectorId,
            mode);

        return new DrmPresenter<DrmPlaneDoubleBufferPresenter, DrmPlaneLastDmaBufferPresenter>(
            primaryPlane,
            primaryPlanePresenter,
            overlayPlane,
            overlayPlanePresenter,
            drmDevice.DeviceFd,
            logger);
    }

    /// <summary>
    /// Creates a DRM presenter with GBM-based primary plane for OpenGL ES rendering.
    /// Uses legacy API with blocking page flips.
    /// </summary>
    /// <exception cref="DrmResourceNotFoundException">Required DRM resources not found</exception>
    /// <exception cref="DrmPlaneNotFoundException">Required plane not found</exception>
    public static DrmPresenter<DrmPlaneGbmPresenter, TOverlay> CreateWithGbmBuffers<TOverlay>(
        DrmDevice drmDevice,
        uint width,
        uint height,
        GbmDevice gbmDevice,
        PixelFormat primaryPlanePixelFormat,
        ILogger logger)
        where TOverlay : class
    {
        ArgumentNullException.ThrowIfNull(drmDevice);
        ArgumentNullException.ThrowIfNull(gbmDevice);
        ArgumentNullException.ThrowIfNull(logger);

        var resources = GetResourcesOrThrow(drmDevice, logger);
        var (primaryPlane, crtcId, connector, mode) = SetupPrimaryPlane(
            drmDevice, resources, width, height, logger);

        var primaryPlanePresenter = new DrmPlaneGbmPresenter(
            drmDevice,
            primaryPlane,
            crtcId,
            width,
            height,
            logger,
            gbmDevice,
            primaryPlanePixelFormat,
            connector.ConnectorId,
            mode);

        return new DrmPresenter<DrmPlaneGbmPresenter, TOverlay>(
            primaryPlane,
            primaryPlanePresenter,
            null,
            null,
            drmDevice.DeviceFd,
            logger);
    }

    /// <summary>
    /// Creates a DRM presenter with atomic GBM-based primary plane for high-performance OpenGL ES rendering.
    /// Uses atomic modesetting with non-blocking page flips and separate page flip thread.
    /// Allows rendering at maximum FPS without vsync blocking.
    /// </summary>
    /// <exception cref="DrmResourceNotFoundException">Required DRM resources not found</exception>
    /// <exception cref="DrmPlaneNotFoundException">Required plane not found</exception>
    public static DrmPresenter<DrmPlaneGbmAtomicPresenter, TOverlay> CreateWithGbmBuffersAtomic<TOverlay>(
        DrmDevice drmDevice,
        uint width,
        uint height,
        GbmDevice gbmDevice,
        PixelFormat primaryPlanePixelFormat,
        ILogger logger)
        where TOverlay : class
    {
        ArgumentNullException.ThrowIfNull(drmDevice);
        ArgumentNullException.ThrowIfNull(gbmDevice);
        ArgumentNullException.ThrowIfNull(logger);

        var resources = GetResourcesOrThrow(drmDevice, logger);
        var (primaryPlane, crtcId, connector, mode) = SetupPrimaryPlane(
            drmDevice, resources, width, height, logger);

        var primaryPlanePresenter = new DrmPlaneGbmAtomicPresenter(
            drmDevice,
            primaryPlane,
            crtcId,
            width,
            height,
            logger,
            gbmDevice,
            primaryPlanePixelFormat,
            connector.ConnectorId,
            mode);

        return new DrmPresenter<DrmPlaneGbmAtomicPresenter, TOverlay>(
            primaryPlane,
            primaryPlanePresenter,
            null,
            null,
            drmDevice.DeviceFd,
            logger);
    }

    /// <summary>
    /// Creates a DRM presenter with atomic GBM-based primary plane and DMA buffer overlay plane.
    /// Primary plane uses atomic modesetting for high-performance OpenGL ES rendering (ImGui, UI).
    /// Overlay plane uses DMA buffers with legacy SetPlane for zero-copy video display.
    /// </summary>
    /// <remarks>
    /// IMPORTANT: Overlay uses legacy mode (useAtomicMode: false) to avoid dual event loop conflict.
    /// The GBM atomic presenter already has an event loop thread for page flip events.
    /// This combination is ideal for applications that need both GPU-rendered UI and hardware-decoded video.
    /// 
    /// Thread Safety:
    /// - Primary plane rendering: OpenGL ES context must be used from a single render thread
    /// - Overlay plane updates: Can be called from video decoder thread (different from render thread)
    /// </remarks>
    /// <exception cref="DrmResourceNotFoundException">Required DRM resources not found</exception>
    /// <exception cref="DrmPlaneNotFoundException">Required plane not found</exception>
    public static DrmPresenter<DrmPlaneGbmAtomicPresenter, DrmPlaneLastDmaBufferPresenter> CreateWithGbmAtomicAndDmaOverlay(
        DrmDevice drmDevice,
        uint width,
        uint height,
        GbmDevice gbmDevice,
        DrmBufferManager bufferManager,
        PixelFormat primaryPlanePixelFormat,
        PixelFormat overlayPlanePixelFormat,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(drmDevice);
        ArgumentNullException.ThrowIfNull(gbmDevice);
        ArgumentNullException.ThrowIfNull(bufferManager);
        ArgumentNullException.ThrowIfNull(logger);

        var resources = GetResourcesOrThrow(drmDevice, logger);
        var (primaryPlane, crtcId, connector, mode) = SetupPrimaryPlane(
            drmDevice, resources, width, height, logger);

        // Create atomic GBM primary plane presenter
        var primaryPlanePresenter = new DrmPlaneGbmAtomicPresenter(
            drmDevice,
            primaryPlane,
            crtcId,
            width,
            height,
            logger,
            gbmDevice,
            primaryPlanePixelFormat,
            connector.ConnectorId,
            mode);

        // Find and create overlay plane presenter
        var overlayPlane = FindOverlayPlaneOrThrow(
            drmDevice, resources, crtcId, overlayPlanePixelFormat, logger);

        logger.LogInformation("Found {Format} overlay plane: ID {PlaneId}",
            overlayPlanePixelFormat.GetName(), overlayPlane.Id);

        // IMPORTANT: Disable atomic mode for overlay to avoid dual event loop conflict
        // The GBM atomic presenter already has an event loop thread
        var overlayPlanePresenter = new DrmPlaneLastDmaBufferPresenter(
            drmDevice,
            overlayPlane,
            crtcId,
            width,
            height,
            bufferManager,
            logger,
            useAtomicMode: false);  // Use legacy SetPlane to avoid event loop conflicts

        return new DrmPresenter<DrmPlaneGbmAtomicPresenter, DrmPlaneLastDmaBufferPresenter>(
            primaryPlane,
            primaryPlanePresenter,
            overlayPlane,
            overlayPlanePresenter,
            drmDevice.DeviceFd,
            logger);
    }

    private static DrmDeviceResources GetResourcesOrThrow(DrmDevice drmDevice, ILogger logger)
    {
        var resources = drmDevice.GetResources();
        if (resources == null)
        {
            logger.LogError("Failed to get DRM resources from device FD {DeviceFd}", drmDevice.DeviceFd);
            throw new DrmResourceNotFoundException(
                "Resources",
                "Failed to get DRM resources",
                drmDevice.DeviceFd);
        }
        return resources;
    }

    private static (DrmPlane primaryPlane, uint crtcId, DrmConnector connector, DrmModeInfo mode) SetupPrimaryPlane(
        DrmDevice drmDevice,
        DrmDeviceResources resources,
        uint width,
        uint height,
        ILogger logger)
    {
        var connector = resources.Connectors.FirstOrDefault(c => c.Connection == DrmModeConnection.Connected);
        if (connector == null)
        {
            logger.LogError("No connected display found on device FD {DeviceFd}", drmDevice.DeviceFd);
            throw new DrmResourceNotFoundException(
                "Connector",
                "No connected display found",
                drmDevice.DeviceFd);
        }

        logger.LogInformation("Found connected display: {Type}", connector.ConnectorType);

        var mode = connector.Modes.FirstOrDefault(m => m.HDisplay == width && m.VDisplay == height);
        if (mode == null)
        {
            logger.LogError(
                "No {Width}x{Height} display mode found on device FD {DeviceFd}. Available modes: {Modes}",
                width, height, drmDevice.DeviceFd,
                string.Join(", ", connector.Modes.Select(m => $"{m.HDisplay}x{m.VDisplay}@{m.VRefresh}Hz")));
            
            throw new DrmModeNotFoundException(width, height, drmDevice.DeviceFd);
        }

        logger.LogInformation("Using mode: {Name} ({Width}x{Height}@{RefreshRate}Hz)",
            mode.Name, mode.HDisplay, mode.VDisplay, mode.VRefresh);

        var encoder = connector.Encoder ?? connector.Encoders.FirstOrDefault();
        if (encoder == null)
        {
            logger.LogError("No encoder found for connector on device FD {DeviceFd}", drmDevice.DeviceFd);
            throw new DrmResourceNotFoundException(
                "Encoder",
                "No encoder found",
                drmDevice.DeviceFd);
        }

        var crtcId = encoder.CrtcId;
        if (crtcId == 0)
        {
            var crtcsArray = resources.Crtcs.ToArray();
            var availableCrtcs = resources.Crtcs
                .Where(crtc => (encoder.PossibleCrtcs & (1u << Array.IndexOf(crtcsArray, crtc))) != 0);
            crtcId = availableCrtcs.FirstOrDefault();
        }

        if (crtcId == 0)
        {
            logger.LogError("No available CRTC found on device FD {DeviceFd}", drmDevice.DeviceFd);
            throw new DrmResourceNotFoundException(
                "CRTC",
                "No available CRTC found",
                drmDevice.DeviceFd);
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
            logger.LogError("No primary plane found on device FD {DeviceFd}", drmDevice.DeviceFd);
            throw new DrmPlaneNotFoundException("primary", null, drmDevice.DeviceFd);
        }

        logger.LogInformation("Found primary plane: ID {PlaneId}", primaryPlane.Id);

#if DEBUG
        var capabilities = drmDevice.GetDeviceCapabilities();
        DumpCapabilities(capabilities, logger);
#endif

        return (primaryPlane, crtcId, connector, mode);
    }

    private static DrmPlane? FindOverlayPlane(
        DrmDeviceResources resources,
        uint crtcId,
        PixelFormat pixelFormat)
    {
        var crtcIndex = resources.Crtcs.ToList().IndexOf(crtcId);
        var compatiblePlanes = resources.Planes
            .Where(p => (p.PossibleCrtcs & (1u << crtcIndex)) != 0)
            .ToList();

        return compatiblePlanes.FirstOrDefault(p =>
        {
            var props = p.GetProperties();
            var typeProp = props.FirstOrDefault(prop => prop.Name.Equals("type", StringComparison.OrdinalIgnoreCase));
            bool isOverlay = typeProp != null && typeProp.EnumNames != null &&
                             typeProp.Value < (ulong)typeProp.EnumNames.Count &&
                             typeProp.EnumNames[(int)typeProp.Value]
                                 .Equals("Overlay", StringComparison.OrdinalIgnoreCase);
            return isOverlay && p.Formats.Contains(pixelFormat.Fourcc);
        });
    }

    private static DrmPlane FindOverlayPlaneOrThrow(
        DrmDevice drmDevice,
        DrmDeviceResources resources,
        uint crtcId,
        PixelFormat pixelFormat,
        ILogger logger)
    {
        var plane = FindOverlayPlane(resources, crtcId, pixelFormat);
        if (plane == null)
        {
            logger.LogError(
                "No overlay plane with {Format} format found on CRTC {CrtcId}, device FD {DeviceFd}",
                pixelFormat.GetName(), crtcId, drmDevice.DeviceFd);
            
            throw new DrmPlaneNotFoundException("overlay", pixelFormat, drmDevice.DeviceFd);
        }
        return plane;
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _logger.LogInformation("Disposing DRM presenter for device FD {DeviceFd}", _drmDeviceFd);

        try
        {
            PrimaryPlanePresenter.Cleanup();
            
            if (OverlayPlanePresenter is DrmSinglePlanePresenter overlayPresenter)
            {
                overlayPresenter.Cleanup();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during DRM presenter cleanup for device FD {DeviceFd}", _drmDeviceFd);
        }
        finally
        {
            _disposed = true;
        }
    }
}
