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
    private readonly DisplayContext _context;
    private readonly DrmCapabilitiesState _capabilities;
    private readonly ILogger _logger;
    private readonly PixelFormat _primaryPlanePixelFormat;
    private readonly uint _width;
    private readonly uint _height;
    private readonly AtomicDisplayManager? _atomicDisplayManager;

    private SharedDmaBuffer? _currentFrame;
    private SharedDmaBuffer? _primaryFrontBuffer;
    private SharedDmaBuffer? _primaryBackBuffer;
    private bool _primaryBufferFlipped = false;

    private DrmPresenter(
        DrmDevice device,
        DrmBufferManager bufferManager,
        DisplayContext context,
        DrmCapabilitiesState capabilities,
        PixelFormat primaryPlanePixelFormat,
        uint width,
        uint height,
        AtomicDisplayManager? atomicDisplayManager,
        ILogger logger)
    {
        _device = device;
        _bufferManager = bufferManager;
        _context = context;
        _capabilities = capabilities;
        _primaryPlanePixelFormat = primaryPlanePixelFormat;
        _width = width;
        _height = height;
        _atomicDisplayManager = atomicDisplayManager;
        _logger = logger;
    }

    public bool TimestampMonotonic => _capabilities.TimestampMonotonic;

    /// <summary>
    /// Gets the ID of the primary plane
    /// </summary>
    public uint GetPrimaryPlaneId() => _context.PrimaryPlane.Id;

    /// <summary>
    /// Gets the ID of the overlay plane
    /// </summary>
    public uint GetOverlayPlaneId() => _context.Nv12Plane.Id;

    public static DrmPresenter Create(
        DrmDevice drmDevice,
        uint width,
        uint height,
        DrmBufferManager bufferManager,
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

        var nv12Format = KnownPixelFormats.DRM_FORMAT_NV12.Fourcc;
        var nv12Plane = compatiblePlanes.FirstOrDefault(p =>
        {
            var props = p.GetProperties();
            var typeProp = props.FirstOrDefault(prop => prop.Name.Equals("type", StringComparison.OrdinalIgnoreCase));
            bool isOverlay = typeProp != null && typeProp.EnumNames != null &&
                             typeProp.Value < (ulong)typeProp.EnumNames.Count &&
                             typeProp.EnumNames[(int)typeProp.Value]
                                 .Equals("Overlay", StringComparison.OrdinalIgnoreCase);
            return isOverlay && p.Formats.Contains(nv12Format);
        });

        if (nv12Plane == null)
        {
            throw new Exception("No NV12-capable overlay plane found");
        }

        logger.LogInformation("Found NV12 overlay plane: ID {PlaneId}", nv12Plane.Id);

        var capabilities = drmDevice.GetDeviceCapabilities();
        AtomicPlaneUpdater? atomicUpdater = null;
        AtomicDisplayManager? atomicDisplayManager = null;
#if DEBUG
        DumpCapabilities(capabilities, logger);
#endif

        // Determine which display method to use:
        // 1. AtomicDisplayManager: When atomic is available but async page flip is NOT supported
        //    (uses VBlank-synchronized event-driven atomic commits)
        // 2. AtomicPlaneUpdater: When atomic async page flip IS supported
        //    (uses async atomic commits)
        // 3. Legacy API: Fallback for older hardware

        // Check if atomic modesetting was successfully enabled (done in Program.cs via DRM_CLIENT_CAP_ATOMIC)
        // We can check this by trying to use atomic API - if it fails, atomic is not supported
        // For now, we assume atomic is enabled if we got this far

        if (!capabilities.AtomicAsyncPageFlip)
        {
            // Atomic is available but async page flip is NOT supported
            // Use event-driven atomic mode with VBlank synchronization
            logger.LogInformation(
                "Using atomic modesetting with VBlank synchronization (async page flip not supported)");

            // Get all required property IDs for the plane
            var fbIdPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, nv12Plane.Id, "FB_ID");
            var crtcIdPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, nv12Plane.Id, "CRTC_ID");
            var crtcXPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, nv12Plane.Id, "CRTC_X");
            var crtcYPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, nv12Plane.Id, "CRTC_Y");
            var crtcWPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, nv12Plane.Id, "CRTC_W");
            var crtcHPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, nv12Plane.Id, "CRTC_H");
            var srcXPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, nv12Plane.Id, "SRC_X");
            var srcYPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, nv12Plane.Id, "SRC_Y");
            var srcWPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, nv12Plane.Id, "SRC_W");
            var srcHPropertyId = GetPlanePropertyId(drmDevice.DeviceFd, nv12Plane.Id, "SRC_H");

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
                nv12Plane.Id,
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
                width, // source width
                height, // source height
                width, // destination width
                height, // destination height
                logger);
        }
        else
        {
            // Atomic async page flip is supported - use AtomicPlaneUpdater
            atomicUpdater = new AtomicPlaneUpdater(drmDevice.DeviceFd, nv12Plane.Id, crtcId);
            logger.LogInformation("Atomic modesetting with async page flip initialized");
        }

        var primaryPlaneFormat = KnownPixelFormats.DRM_FORMAT_XRGB8888;
        var rgbBuf = bufferManager.AllocateBuffer(width, height, primaryPlaneFormat);
        rgbBuf.MapBuffer();
        if (rgbBuf.MapStatus == MapStatus.FailedToMap)
        {
            logger.LogError("Failed to mmap RGB buffer");
            rgbBuf.Dispose();
            return null;
        }

        rgbBuf.DmaBuffer.GetMappedSpan().Fill(0);
        rgbBuf.DmaBuffer.SyncMap();
        logger.LogInformation("Created and filled RGB buffer");


        var (rgbFbId, _) = CreateRgbFramebuffer(drmDevice, rgbBuf, width, height, primaryPlaneFormat, logger);
        if (rgbFbId == 0)
        {
            rgbBuf.Dispose();
            return null;
        }

        if (!SetCrtcMode(drmDevice, crtcId, connector.ConnectorId, rgbFbId, mode, width, height, logger))
        {
            LibDrm.drmModeRmFB(drmDevice.DeviceFd, rgbFbId);
            rgbBuf.Dispose();
            return null;
        }

        if (!SetPlane(drmDevice.DeviceFd, primaryPlane.Id, crtcId, rgbFbId, width, height, width, height, null, false,
                logger))
        {
            LibDrm.drmModeRmFB(drmDevice.DeviceFd, rgbFbId);
            rgbBuf.Dispose();
            return null;
        }

        var context = new DisplayContext
        {
            CrtcId = crtcId,
            ConnectorId = connector.ConnectorId,
            PrimaryPlane = primaryPlane,
            Nv12Plane = nv12Plane,
            RgbBuffer = rgbBuf,
            RgbFbId = rgbFbId,
            Nv12FbId = 0,
            AtomicUpdater = atomicUpdater,
        };

        logger.LogInformation("Display setup complete");
        return new(drmDevice, bufferManager, context, capabilities, primaryPlaneFormat, width, height,
            atomicDisplayManager, logger);
    }

    /// <summary>
    /// Initializes double buffering for primary plane with ARGB8888 format.
    /// Must be called after Create() and before using SetPrimaryPlaneBuffer().
    /// </summary>
    public bool InitializePrimaryPlaneDoubleBuffering()
    {
        // Create two buffers for double buffering
        var argb8888Format = KnownPixelFormats.DRM_FORMAT_ARGB8888;

        _primaryFrontBuffer = _bufferManager.AllocateBuffer(_width, _height, argb8888Format);
        _primaryFrontBuffer.MapBuffer();
        if (_primaryFrontBuffer.MapStatus == MapStatus.FailedToMap)
        {
            _logger.LogError("Failed to map primary front buffer");
            _primaryFrontBuffer.Dispose();
            _primaryFrontBuffer = null;
            return false;
        }

        _primaryBackBuffer = _bufferManager.AllocateBuffer(_width, _height, argb8888Format);
        _primaryBackBuffer.MapBuffer();
        if (_primaryBackBuffer.MapStatus == MapStatus.FailedToMap)
        {
            _logger.LogError("Failed to map primary back buffer");
            _primaryBackBuffer.Dispose();
            _primaryFrontBuffer?.Dispose();
            _primaryBackBuffer = null;
            _primaryFrontBuffer = null;
            return false;
        }

        _logger.LogInformation("Primary plane double buffering initialized with ARGB8888 format");
        return true;
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
    /// Gets the current back buffer for primary plane rendering.
    /// After filling, call SwapPrimaryPlaneBuffers() to present it.
    /// </summary>
    public Span<byte> GetPrimaryPlaneBackBuffer()
    {
        if (_primaryBackBuffer == null)
        {
            throw new InvalidOperationException(
                "Primary plane double buffering not initialized. Call InitializePrimaryPlaneDoubleBuffering() first.");
        }

        return _primaryBackBuffer.DmaBuffer.GetMappedSpan();
    }

    /// <summary>
    /// Swaps primary plane buffers and presents the back buffer.
    /// </summary>
    public bool SwapPrimaryPlaneBuffers()
    {
        if (_primaryFrontBuffer == null || _primaryBackBuffer == null)
        {
            _logger.LogError("Primary plane buffers not initialized");
            return false;
        }

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
            _context.PrimaryPlane.Id,
            _context.CrtcId,
            _primaryBackBuffer.FramebufferId,
            _width,
            _height,
            _width,
            _height,
            _context.AtomicUpdater,
            _capabilities.AsyncPageFlip,
            _logger);

        if (success)
        {
            // Swap buffers
            (_primaryFrontBuffer, _primaryBackBuffer) = (_primaryBackBuffer, _primaryFrontBuffer);
            _primaryBufferFlipped = true;
        }

        return success;
    }

    public bool SetOverlayPlaneBuffer(
        SharedDmaBuffer drmBuffer)
    {
        if (drmBuffer.FramebufferId == 0)
        {
            drmBuffer.FramebufferId = _bufferManager.CreateFramebuffer(drmBuffer);
        }

        // Use atomic display manager if available
        if (_atomicDisplayManager != null)
        {
            _atomicDisplayManager.SubmitFrame(drmBuffer, drmBuffer.FramebufferId);
            return true;
        }

        // Legacy path
        if (_currentFrame != null)
        {
            _processedBuffers.Add(_currentFrame);
        }

        _currentFrame = drmBuffer;

        return SetPlane(
            _device.DeviceFd,
            _context.Nv12Plane.Id,
            _context.CrtcId,
            drmBuffer.FramebufferId,
            drmBuffer.Width,
            drmBuffer.Height,
            _width,
            _height,
            _context.AtomicUpdater,
            _capabilities.AsyncPageFlip,
            _logger);
    }

    public SharedDmaBuffer[] GetPresentedOverlayBuffers()
    {
        // Use atomic display manager if available
        if (_atomicDisplayManager != null)
        {
            return _atomicDisplayManager.GetCompletedBuffers();
        }

        // Legacy path
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
        // srcWidth/srcHeight: dimensions of the framebuffer (may be padded, e.g., 1920x1088)
        // dstWidth/dstHeight: dimensions to display on screen (e.g., 1920x1080)

        // Try atomic API first if available
        if (atomicUpdater != null)
        {
            var success = atomicUpdater.UpdatePlane(
                planeId,
                crtcId,
                fbId,
                0, 0, // crtcX, crtcY
                dstWidth, dstHeight,
                0, 0, // srcX, srcY (16.16 fixed point, but we pass 0)
                srcWidth << 16, srcHeight << 16, // srcW, srcH in 16.16 fixed point
                tryAsync);

            if (success)
                return true;

            // Fall through to legacy API if atomic fails
            logger.LogWarning("Atomic plane update failed, falling back to legacy API");
        }

        // Legacy API fallback
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

    /// <summary>
    /// Cleans up display resources including framebuffers and DMA buffers.
    /// </summary>
    /// <summary>
    /// Cleans up display resources including framebuffers and DMA buffers.
    /// </summary>
    public void CleanupDisplay()
    {

        _logger.LogInformation("Cleaning up display resources");

        try
        {
            if (_atomicDisplayManager != null)
            {
                _atomicDisplayManager.Dispose();
            }

            // Clean up primary plane double buffers
            if (_primaryFrontBuffer != null)
            {
                if (_primaryFrontBuffer.FramebufferId != 0)
                {
                    LibDrm.drmModeRmFB(_device.DeviceFd, _primaryFrontBuffer.FramebufferId);
                }

                _primaryFrontBuffer.DmaBuffer.UnmapBuffer();
                _primaryFrontBuffer.Dispose();
            }

            if (_primaryBackBuffer != null)
            {
                if (_primaryBackBuffer.FramebufferId != 0)
                {
                    LibDrm.drmModeRmFB(_device.DeviceFd, _primaryBackBuffer.FramebufferId);
                }

                _primaryBackBuffer.DmaBuffer.UnmapBuffer();
                _primaryBackBuffer.Dispose();
            }

            if (_context.Nv12FbId != 0)
            {
                var result = LibDrm.drmModeRmFB(_device.DeviceFd, _context.Nv12FbId);
                if (result != 0)
                {
                    _logger.LogWarning("Failed to remove NV12 framebuffer {FbId}: {Result}",
                        _context.Nv12FbId, result);
                }
            }

            if (_context.RgbFbId != 0)
            {
                var result = LibDrm.drmModeRmFB(_device.DeviceFd, _context.RgbFbId);
                if (result != 0)
                {
                    _logger.LogWarning("Failed to remove RGB framebuffer {FbId}: {Result}",
                        _context.RgbFbId, result);
                }
            }

            if (_context.RgbBuffer != null)
            {
                _context.RgbBuffer.DmaBuffer.UnmapBuffer();
                _context.RgbBuffer.Dispose();
            }

            if (_context.AtomicUpdater != null)
            {
                _context.AtomicUpdater.Dispose();
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

    private static unsafe (uint fbId, uint handle) CreateRgbFramebuffer(
        DrmDevice drmDevice,
        SharedDmaBuffer rgbBuf,
        uint width,
        uint height,
        PixelFormat rgbFormat,
        ILogger logger)
    {
        var result = LibDrm.drmPrimeFDToHandle(drmDevice.DeviceFd, rgbBuf.DmaBuffer.Fd, out uint rgbHandle);
        if (result != 0)
        {
            logger.LogError("Failed to convert RGB DMA FD to handle: {Result}", result);
            return (0, 0);
        }


        uint rgbPitch = (uint)(width * 4);
        uint* rgbHandles = stackalloc uint[4] { rgbHandle, 0, 0, 0 };
        uint* rgbPitches = stackalloc uint[4] { rgbPitch, 0, 0, 0 };
        uint* rgbOffsets = stackalloc uint[4] { 0, 0, 0, 0 };

        var resultFb = LibDrm.drmModeAddFB2(
            drmDevice.DeviceFd,
            width,
            height,
            rgbFormat.Fourcc,
            rgbHandles,
            rgbPitches,
            rgbOffsets,
            out var rgbFbId,
            0);
        if (resultFb != 0)
        {
            logger.LogError("Failed to create RGB framebuffer: {Result}", resultFb);
            return (0, 0);
        }

        logger.LogInformation("Created RGB framebuffer with ID: {FbId}", rgbFbId);
        return (rgbFbId, rgbHandle);
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

    private class DisplayContext
    {
        public required uint CrtcId { get; init; }
        public required uint ConnectorId { get; init; }
        public required DrmPlane PrimaryPlane { get; init; }
        public required DrmPlane Nv12Plane { get; init; }
        public SharedDmaBuffer RgbBuffer { get; set; }
        public uint RgbFbId { get; set; }
        public uint Nv12FbId { get; set; }
        public AtomicPlaneUpdater? AtomicUpdater { get; set; }
    }
}