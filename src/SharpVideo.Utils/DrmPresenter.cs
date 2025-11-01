using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;

namespace SharpVideo.Utils;

[SupportedOSPlatform("linux")]
public class DrmPresenter
{
    private readonly List<SharedDmaBuffer> _processedBuffers = new();
    private readonly DrmDevice _device;
    private readonly DrmBufferManager _bufferManager;
    private readonly DrmCapabilitiesState _capabilities;
    private readonly ILogger _logger;
    private readonly PixelFormat _primaryPlanePixelFormat;
    private readonly uint _width;
    private readonly uint _height;
    private readonly uint _crtcId;
    private readonly AtomicDisplayManager? _atomicDisplayManager;
    private readonly DrmPlane _primaryPlane;
    private readonly DrmPlane _overlayPlane;
    private readonly AtomicPlaneUpdater? _atomicUpdater;

    private SharedDmaBuffer? _currentFrame;
    private SharedDmaBuffer _primaryFrontBuffer;
    private SharedDmaBuffer _primaryBackBuffer;

    private DrmPresenter(
        DrmDevice device,
        DrmPlane primaryPlane,
        DrmPlane overlayPlane,
        uint crtcId,
        DrmBufferManager bufferManager,
        AtomicPlaneUpdater? atomicUpdater,
        DrmCapabilitiesState capabilities,
        PixelFormat primaryPlanePixelFormat,
        uint width,
        uint height,
        SharedDmaBuffer primaryFrontBuffer,
        SharedDmaBuffer primaryBackBuffer,
        AtomicDisplayManager? atomicDisplayManager,
        ILogger logger)
    {
        _device = device;
        _primaryPlane = primaryPlane;
        _overlayPlane = overlayPlane;
        _crtcId = crtcId;
        _bufferManager = bufferManager;
        _atomicUpdater = atomicUpdater;
        _capabilities = capabilities;
        _primaryPlanePixelFormat = primaryPlanePixelFormat;
        _width = width;
        _height = height;
        _primaryFrontBuffer = primaryFrontBuffer;
        _primaryBackBuffer = primaryBackBuffer;
        _atomicDisplayManager = atomicDisplayManager;
        _logger = logger;
    }

    public bool TimestampMonotonic => _capabilities.TimestampMonotonic;

    /// <summary>
    /// Gets the ID of the primary plane
    /// </summary>
    public uint GetPrimaryPlaneId() => _primaryPlane.Id;

    /// <summary>
    /// Gets the ID of the overlay plane
    /// </summary>
    public uint GetOverlayPlaneId() => _overlayPlane.Id;

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

            var fbIdPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, overlayPlane.Id, "FB_ID");
            var crtcIdPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, overlayPlane.Id, "CRTC_ID");
            var crtcXPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, overlayPlane.Id, "CRTC_X");
            var crtcYPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, overlayPlane.Id, "CRTC_Y");
            var crtcWPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, overlayPlane.Id, "CRTC_W");
            var crtcHPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, overlayPlane.Id, "CRTC_H");
            var srcXPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, overlayPlane.Id, "SRC_X");
            var srcYPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, overlayPlane.Id, "SRC_Y");
            var srcWPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, overlayPlane.Id, "SRC_W");
            var srcHPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, overlayPlane.Id, "SRC_H");

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

        var primaryFrontBuffer = bufferManager.AllocateBuffer(width, height, primaryPlanePixelFormat);
        primaryFrontBuffer.MapBuffer();
        if (primaryFrontBuffer.MapStatus == MapStatus.FailedToMap)
        {
            logger.LogError("Failed to map primary front buffer");
            primaryFrontBuffer.Dispose();
            return null;
        }

        var primaryBackBuffer = bufferManager.AllocateBuffer(width, height, primaryPlanePixelFormat);
        primaryBackBuffer.MapBuffer();
        if (primaryBackBuffer.MapStatus == MapStatus.FailedToMap)
        {
            logger.LogError("Failed to map primary back buffer");
            primaryBackBuffer.Dispose();
            primaryFrontBuffer.DmaBuffer.UnmapBuffer();
            primaryFrontBuffer.Dispose();
            return null;
        }

        // Initialize front buffer with black/transparent
        primaryFrontBuffer.DmaBuffer.GetMappedSpan().Fill(0);
        primaryFrontBuffer.DmaBuffer.SyncMap();

        // Create framebuffer for front buffer
        var (primaryFbId, _) = CreateFramebuffer(drmDevice, primaryFrontBuffer, width, height,
            primaryPlanePixelFormat, logger);
        if (primaryFbId == 0)
        {
            primaryBackBuffer.DmaBuffer.UnmapBuffer();
            primaryBackBuffer.Dispose();
            primaryFrontBuffer.DmaBuffer.UnmapBuffer();
            primaryFrontBuffer.Dispose();
            return null;
        }

        primaryFrontBuffer.FramebufferId = primaryFbId;
        logger.LogInformation("Created primary plane double buffers");

        // Set CRTC mode with primary plane
        if (!SetCrtcMode(drmDevice, crtcId, connector.ConnectorId, primaryFbId, mode, width, height, logger))
        {
            LibDrm.drmModeRmFB(drmDevice.DeviceFd, primaryFbId);
            primaryBackBuffer.DmaBuffer.UnmapBuffer();
            primaryBackBuffer.Dispose();
            primaryFrontBuffer.DmaBuffer.UnmapBuffer();
            primaryFrontBuffer.Dispose();
            return null;
        }

        // Set primary plane with initial buffer
        if (!SetPlane(drmDevice.DeviceFd, primaryPlane.Id, crtcId, primaryFbId, width, height, width, height,
                null, false, logger))
        {
            LibDrm.drmModeRmFB(drmDevice.DeviceFd, primaryFbId);
            primaryBackBuffer.DmaBuffer.UnmapBuffer();
            primaryBackBuffer.Dispose();
            primaryFrontBuffer.DmaBuffer.UnmapBuffer();
            primaryFrontBuffer.Dispose();
            return null;
        }

        logger.LogInformation("Display setup complete with double buffering");
        return new DrmPresenter(
            drmDevice,
            primaryPlane,
            overlayPlane,
            crtcId,
            bufferManager,
            atomicUpdater,
            capabilities,
            primaryPlanePixelFormat,
            width,
            height,
            primaryFrontBuffer,
            primaryBackBuffer,
            atomicDisplayManager,
            logger);
    }

    /// <summary>
    /// Sets the z-position property for a plane to control layering order.
    /// Lower z-pos values are displayed behind higher values.
    /// </summary>
    public bool SetPlaneZPosition(uint planeId, ulong zpos)
    {
        var zposPropertyId = GetPlanePropertyId(_device.DeviceFd, planeId, "zpos");
        if (zposPropertyId == 0)
        {
            _logger.LogWarning("Plane {PlaneId} does not have zpos property", planeId);
            return false;
        }

        var result = LibDrm.drmModeObjectSetProperty(
            _device.DeviceFd,
            planeId,
            LibDrm.DRM_MODE_OBJECT_PLANE,
            zposPropertyId,
            zpos);

        if (result != 0)
        {
            _logger.LogError("Failed to set zpos={Zpos} for plane {PlaneId}: {Result}", zpos, planeId, result);
            return false;
        }

        _logger.LogInformation("Set plane {PlaneId} zpos to {Zpos}", planeId, zpos);
        return true;
    }

    /// <summary>
    /// Gets the valid range for the zpos property of a plane.
    /// Returns (min, max, current) or null if zpos is not supported.
    /// </summary>
    public unsafe (ulong min, ulong max, ulong current)? GetPlaneZPositionRange(uint planeId)
    {
        var props = LibDrm.drmModeObjectGetProperties(_device.DeviceFd, planeId, LibDrm.DRM_MODE_OBJECT_PLANE);
        if (props == null)
            return null;

        try
        {
            for (int i = 0; i < props->CountProps; i++)
            {
                var propId = props->Props[i];
                var prop = LibDrm.drmModeGetProperty(_device.DeviceFd, propId);
                if (prop == null)
                    continue;

                try
                {
                    var name = prop->NameString;
                    if (name != null && name.Equals("zpos", StringComparison.OrdinalIgnoreCase))
                    {
                        var currentValue = props->PropValues[i];

                        // For range properties, values[0] is min and values[1] is max
                        if (prop->CountValues >= 2)
                        {
                            var min = prop->Values[0];
                            var max = prop->Values[1];
                            _logger.LogDebug("Plane {PlaneId} zpos range: [{Min}, {Max}], current: {Current}",
                                planeId, min, max, currentValue);
                            return (min, max, currentValue);
                        }
                    }
                }
                finally
                {
                    LibDrm.drmModeFreeProperty(prop);
                }
            }
        }
        finally
        {
            LibDrm.drmModeFreeObjectProperties(props);
        }

        return null;
    }

    /// <summary>
    /// Gets the current back buffer for primary plane rendering.
    /// After filling, call SwapPrimaryPlaneBuffers() to present it.
    /// </summary>
    public Span<byte> GetPrimaryPlaneBackBuffer()
    {
        return _primaryBackBuffer.DmaBuffer.GetMappedSpan();
    }

    /// <summary>
    /// Gets the current back buffer as SharedDmaBuffer for direct GPU rendering (e.g., OpenGL ES).
    /// This allows zero-copy rendering by providing access to the DMA-BUF file descriptor.
    /// After rendering, call SwapPrimaryPlaneBuffers() to present it.
    /// </summary>
    public SharedDmaBuffer GetPrimaryPlaneBackBufferDma()
    {
      return _primaryBackBuffer;
    }

    /// <summary>
    /// Swaps primary plane buffers and presents the back buffer.
    /// </summary>
    public bool SwapPrimaryPlaneBuffers()
    {
        // Sync the back buffer before presenting
        _primaryBackBuffer.DmaBuffer.SyncMap();

        // Create framebuffer for back buffer if needed
        if (_primaryBackBuffer.FramebufferId == 0)
        {
            _primaryBackBuffer.FramebufferId = _bufferManager.CreateFramebuffer(_primaryBackBuffer);
        }

        // Update the plane to show the back buffer
        var success = SetPlane(
            _device.DeviceFd,
            _primaryPlane.Id,
            _crtcId,
            _primaryBackBuffer.FramebufferId,
            _width,
            _height,
            _width,
            _height,
            _atomicUpdater,
            _capabilities.AsyncPageFlip,
            _logger);

        if (success)
        {
            // Swap buffers
            (_primaryFrontBuffer, _primaryBackBuffer) = (_primaryBackBuffer, _primaryFrontBuffer);
        }

        return success;
    }

    public bool SetOverlayPlaneBuffer(SharedDmaBuffer drmBuffer)
    {
        if (drmBuffer.FramebufferId == 0)
        {
            drmBuffer.FramebufferId = _bufferManager.CreateFramebuffer(drmBuffer);
        }

        if (_atomicDisplayManager != null)
        {
            _atomicDisplayManager.SubmitFrame(drmBuffer, drmBuffer.FramebufferId);
            return true;
        }

        if (_currentFrame != null)
        {
            _processedBuffers.Add(_currentFrame);
        }

        _currentFrame = drmBuffer;

        return SetPlane(
            _device.DeviceFd,
            _overlayPlane.Id,
            _crtcId,
            drmBuffer.FramebufferId,
            drmBuffer.Width,
            drmBuffer.Height,
            _width,
            _height,
            _atomicUpdater,
            _capabilities.AsyncPageFlip,
            _logger);
    }

    public SharedDmaBuffer[] GetPresentedOverlayBuffers()
    {
        if (_atomicDisplayManager != null)
        {
            return _atomicDisplayManager.GetCompletedBuffers();
        }

        var ret = _processedBuffers.ToArray();
        _processedBuffers.Clear();
        return ret;
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

    private static bool SetPlane(
        int drmFd,
        uint planeId,
        uint crtcId,
        uint fbId,
        uint srcWidth,
        uint srcHeight,
        uint dstWidth,
        uint dstHeight,
        AtomicPlaneUpdater? atomicUpdater,
        bool tryAsync,
        ILogger logger)
    {
        if (atomicUpdater != null)
        {
            var success = atomicUpdater.UpdatePlane(
                planeId,
                crtcId,
                fbId,
                0, 0,
                dstWidth, dstHeight,
                0, 0,
                srcWidth << 16, srcHeight << 16,
                tryAsync);

            if (success)
                return true;

            logger.LogWarning("Atomic plane update failed, falling back to legacy API");
        }

        var result = LibDrm.drmModeSetPlane(drmFd, planeId, crtcId, fbId, 0,
            0, 0, dstWidth, dstHeight,
            0, 0, srcWidth << 16, srcHeight << 16);
        if (result != 0)
        {
            logger.LogError("Failed to set plane {PlaneId}: {Result}", planeId, result);
            return false;
        }

        return true;
    }

    public void CleanupDisplay()
    {
        _logger.LogInformation("Cleaning up display resources");

        try
        {
            if (_atomicDisplayManager != null)
            {
                _atomicDisplayManager.Dispose();
            }

            if (_primaryFrontBuffer.FramebufferId != 0)
            {
                LibDrm.drmModeRmFB(_device.DeviceFd, _primaryFrontBuffer.FramebufferId);
            }

            _primaryFrontBuffer.DmaBuffer.UnmapBuffer();
            _primaryFrontBuffer.Dispose();

            if (_primaryBackBuffer.FramebufferId != 0)
            {
                LibDrm.drmModeRmFB(_device.DeviceFd, _primaryBackBuffer.FramebufferId);
            }

            _primaryBackBuffer.DmaBuffer.UnmapBuffer();
            _primaryBackBuffer.Dispose();

            if (_atomicUpdater != null)
            {
                _atomicUpdater.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during display cleanup");
        }
    }

    private static unsafe bool SetCrtcMode(
        DrmDevice drmDevice,
        uint crtcId,
        uint connectorId,
        uint fbId,
        DrmModeInfo mode,
        uint width,
        uint height,
        ILogger logger)
    {
        var nativeMode = new DrmModeModeInfo
        {
            Clock = mode.Clock,
            HDisplay = mode.HDisplay,
            HSyncStart = mode.HSyncStart,
            HSyncEnd = mode.HSyncEnd,
            HTotal = mode.HTotal,
            HSkew = mode.HSkew,
            VDisplay = mode.VDisplay,
            VSyncStart = mode.VSyncStart,
            VSyncEnd = mode.VSyncEnd,
            VTotal = mode.VTotal,
            VScan = mode.VScan,
            VRefresh = mode.VRefresh,
            Flags = mode.Flags,
            Type = mode.Type
        };

        var nameBytes = System.Text.Encoding.UTF8.GetBytes(mode.Name);
        for (int i = 0; i < Math.Min(nameBytes.Length, 32); i++)
        {
            nativeMode.Name[i] = nameBytes[i];
        }

        var result = LibDrm.drmModeSetCrtc(drmDevice.DeviceFd, crtcId, fbId, 0, 0, &connectorId, 1, &nativeMode);
        if (result != 0)
        {
            logger.LogError("Failed to set CRTC mode: {Result}", result);
            return false;
        }

        logger.LogInformation("Successfully set CRTC to mode {Name}", mode.Name);
        return true;
    }

    private static unsafe (uint fbId, uint handle) CreateFramebuffer(
        DrmDevice drmDevice,
        SharedDmaBuffer buffer,
        uint width,
        uint height,
        PixelFormat format,
        ILogger logger)
    {
        var result = LibDrm.drmPrimeFDToHandle(drmDevice.DeviceFd, buffer.DmaBuffer.Fd, out uint handle);
        if (result != 0)
        {
            logger.LogError("Failed to convert DMA FD to handle for {Format}: {Result}", format.GetName(), result);
            return (0, 0);
        }

        // Get buffer parameters from BuffersInfoProvider
        var bufferParams = BuffersInfoProvider.GetBufferParams(width, height, format);

        uint* handles = stackalloc uint[4];
        uint* pitches = stackalloc uint[4];
        uint* offsets = stackalloc uint[4];

        // Configure handles, pitches and offsets based on plane count
        for (int i = 0; i < bufferParams.PlanesCount; i++)
        {
            handles[i] = handle; // Same handle for all planes in contiguous buffer
            pitches[i] = buffer.Stride > 0 ? buffer.Stride : bufferParams.Stride;
            offsets[i] = (uint)bufferParams.PlaneOffsets[i];
        }

        // Fill remaining slots with zeros
        for (int i = bufferParams.PlanesCount; i < 4; i++)
        {
            handles[i] = 0;
            pitches[i] = 0;
            offsets[i] = 0;
        }

        var resultFb = LibDrm.drmModeAddFB2(
            drmDevice.DeviceFd,
            width,
            height,
            format.Fourcc,
            handles,
            pitches,
            offsets,
            out var fbId,
            0);

        if (resultFb != 0)
        {
            logger.LogError("Failed to create {Format} framebuffer: {Result}", format.GetName(), resultFb);
            return (0, 0);
        }

        logger.LogTrace("Created {Format} framebuffer with ID: {FbId}", format.GetName(), fbId);
        return (fbId, handle);
    }

    private static unsafe uint GetPlanePropertyId(int drmFd, uint planeId, string propertyName)
    {
        var props = LibDrm.drmModeObjectGetProperties(drmFd, planeId, LibDrm.DRM_MODE_OBJECT_PLANE);
        if (props == null)
            return 0;

        try
        {
            for (int i = 0; i < props->CountProps; i++)
            {
                var propId = props->Props[i];
                var prop = LibDrm.drmModeGetProperty(drmFd, propId);
                if (prop == null)
                    continue;

                try
                {
                    var name = prop->NameString;
                    if (name != null && name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        return propId;
                    }
                }
                finally
                {
                    LibDrm.drmModeFreeProperty(prop);
                }
            }
        }
        finally
        {
            LibDrm.drmModeFreeObjectProperties(props);
        }

        return 0;
    }
}
