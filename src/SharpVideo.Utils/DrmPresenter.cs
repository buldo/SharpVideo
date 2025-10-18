using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;

namespace SharpVideo.Utils;

[SupportedOSPlatform("linux")]
public class DrmPresenter
{
    private readonly DrmDevice _device;
    private readonly DisplayContext _context;
    private readonly ILogger _logger;

    private DrmPresenter(
        DrmDevice device,
        DisplayContext context,
        ILogger logger)
    {
        _device = device;
        _context = context;
        _logger = logger;
    }

    public static DrmPresenter Create(
        DrmDevice drmDevice,
        int width,
        int height,
        DmaBuffersAllocator allocator,
        DrmCapabilitiesState capabilities,
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

        // Initialize atomic plane updater for better performance
        // Note: Atomic modesetting may not work on all hardware/kernels
        // Falls back to legacy API if atomic commits fail at runtime
        AtomicPlaneUpdater? atomicUpdater = null;
        try
        {
            atomicUpdater = new AtomicPlaneUpdater(drmDevice.DeviceFd, nv12Plane.Id, crtcId);
            logger.LogInformation(
                "✓ Atomic modesetting infrastructure initialized (will attempt atomic plane updates)");
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not initialize atomic modesetting infrastructure: {Error}", ex.Message);
        }

        // Setup RGB buffer for mode setting
        ulong rgbBufferSize = (ulong)(width * height * 4);
        var rgbBuf = allocator.Allocate(rgbBufferSize);
        if (rgbBuf == null)
        {
            logger.LogError("Failed to allocate RGB buffer");
            return null;
        }

        rgbBuf.MapBuffer();
        if (rgbBuf.MapStatus == MapStatus.FailedToMap)
        {
            logger.LogError("Failed to mmap RGB buffer");
            rgbBuf.Dispose();
            return null;
        }

        rgbBuf.GetMappedSpan().Fill(0);
        rgbBuf.SyncMap();
        logger.LogInformation("Created and filled RGB buffer");

        var (rgbFbId, _) = CreateRgbFramebuffer(drmDevice, rgbBuf, width, height, logger);
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

        // Log which optimizations are being used
        if (capabilities.AddFB2Modifiers)
        {
            logger.LogInformation(
                "Display configured with format modifier support (tiling/compression optimization available)");
        }

        if (capabilities.TimestampMonotonic)
        {
            logger.LogInformation("Display using monotonic timestamps for precise timing");
        }

        logger.LogInformation("Display setup complete");

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
            SupportsAsyncFlip = capabilities.AsyncPageFlip
        };

        return new(drmDevice, context, logger);
    }

    public bool SetOverlayPlane(
        ManagedDrmBuffer drmBuffer,
        int dstWidth,
        int dstHeight
    )
    {
        return SetPlane(
            _device.DeviceFd,
            _context.Nv12Plane.Id,
            _context.CrtcId,
            drmBuffer.FramebufferId,
            drmBuffer.Width,
            drmBuffer.Height,
            dstWidth,
            dstHeight,
            _context.AtomicUpdater,
            _context.SupportsAsyncFlip,
            _logger);
    }

    private static unsafe bool SetPlane(
        int drmFd,
        uint planeId,
        uint crtcId,
        uint fbId,
        int srcWidth,
        int srcHeight,
        int dstWidth,
        int dstHeight,
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
                (uint)dstWidth, (uint)dstHeight,
                0, 0, // srcX, srcY (16.16 fixed point, but we pass 0)
                (uint)srcWidth << 16, (uint)srcHeight << 16, // srcW, srcH in 16.16 fixed point
                tryAsync);

            if (success)
                return true;

            // Fall through to legacy API if atomic fails
            logger.LogWarning("Atomic plane update failed, falling back to legacy API");
        }

        // Legacy API fallback
        var result = LibDrm.drmModeSetPlane(drmFd, planeId, crtcId, fbId, 0,
            0, 0, (uint)dstWidth, (uint)dstHeight,
            0, 0, (uint)srcWidth << 16, (uint)srcHeight << 16);
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
                _context.RgbBuffer.UnmapBuffer();
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
        int width,
        int height,
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
        DmaBuffers.DmaBuffer rgbBuf,
        int width,
        int height,
        ILogger logger)
    {
        var result = LibDrm.drmPrimeFDToHandle(drmDevice.DeviceFd, rgbBuf.Fd, out uint rgbHandle);
        if (result != 0)
        {
            logger.LogError("Failed to convert RGB DMA FD to handle: {Result}", result);
            return (0, 0);
        }

        var rgbFormat = KnownPixelFormats.DRM_FORMAT_XRGB8888.Fourcc;
        uint rgbPitch = (uint)(width * 4);
        uint* rgbHandles = stackalloc uint[4] { rgbHandle, 0, 0, 0 };
        uint* rgbPitches = stackalloc uint[4] { rgbPitch, 0, 0, 0 };
        uint* rgbOffsets = stackalloc uint[4] { 0, 0, 0, 0 };

        var resultFb = LibDrm.drmModeAddFB2(drmDevice.DeviceFd, (uint)width, (uint)height, rgbFormat,
            rgbHandles, rgbPitches, rgbOffsets, out var rgbFbId, 0);
        if (resultFb != 0)
        {
            logger.LogError("Failed to create RGB framebuffer: {Result}", resultFb);
            return (0, 0);
        }

        logger.LogInformation("Created RGB framebuffer with ID: {FbId}", rgbFbId);
        return (rgbFbId, rgbHandle);
    }

    private class DisplayContext
    {
        public required uint CrtcId { get; init; }
        public required uint ConnectorId { get; init; }
        public required DrmPlane PrimaryPlane { get; init; }
        public required DrmPlane Nv12Plane { get; init; }
        public DmaBuffers.DmaBuffer? RgbBuffer { get; set; }
        public uint RgbFbId { get; set; }
        public uint Nv12FbId { get; set; }
        public AtomicPlaneUpdater? AtomicUpdater { get; set; }
        public bool SupportsAsyncFlip { get; set; }
    }
}