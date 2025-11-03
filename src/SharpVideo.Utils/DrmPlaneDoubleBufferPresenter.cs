using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;

namespace SharpVideo.Utils;

[SupportedOSPlatform("linux")]
public class DrmPlaneDoubleBufferPresenter : DrmSinglePlanePresenter
{
    private readonly DrmBufferManager _bufferManager;
    private SharedDmaBuffer _primaryFrontBuffer;
    private SharedDmaBuffer _primaryBackBuffer;

    public DrmPlaneDoubleBufferPresenter(
        DrmDevice drmDevice,
        DrmPlane plane,
        uint crtcId,
        uint width,
        uint height,
        DrmCapabilitiesState capabilities,
        ILogger logger,
        DrmBufferManager bufferManager,
        PixelFormat primaryPlanePixelFormat,
        uint connectorId,
        DrmModeInfo mode)
        : base(drmDevice, plane, crtcId, width, height, capabilities, logger)
    {
        _bufferManager = bufferManager;

        _primaryFrontBuffer = bufferManager.AllocateBuffer(width, height, primaryPlanePixelFormat);
        _primaryFrontBuffer.MapBuffer();
        if (_primaryFrontBuffer.MapStatus == MapStatus.FailedToMap)
        {
            LocalCleanup();
            throw new Exception("Failed to map primary front buffer");
        }

        _primaryBackBuffer = bufferManager.AllocateBuffer(width, height, primaryPlanePixelFormat);
        _primaryBackBuffer.MapBuffer();
        if (_primaryBackBuffer.MapStatus == MapStatus.FailedToMap)
        {
            LocalCleanup();
            throw new Exception("Failed to map primary back buffer");
        }

        // Initialize front buffer with black/transparent
        _primaryFrontBuffer.DmaBuffer.GetMappedSpan().Fill(0);
        _primaryFrontBuffer.DmaBuffer.SyncMap();

        // Create framebuffer for front buffer
        var (primaryFbId, _) = CreateFramebuffer(
            drmDevice,
            _primaryFrontBuffer,
            width,
            height,
            primaryPlanePixelFormat,
            logger);
        if (primaryFbId == 0)
        {
            LocalCleanup();
            throw new Exception();
        }

        _primaryFrontBuffer.FramebufferId = primaryFbId;
        logger.LogInformation("Created primary plane double buffers");

        // Set CRTC mode with primary plane
        if (!SetCrtcMode(drmDevice, crtcId, connectorId, primaryFbId, mode, width, height, logger))
        {
            LibDrm.drmModeRmFB(drmDevice.DeviceFd, primaryFbId);
            LocalCleanup();
            throw new Exception("Failed to set crtc");
        }

        // Set primary plane with initial buffer
        if (!SetPlane(primaryFbId, width, height))
        {
            LibDrm.drmModeRmFB(drmDevice.DeviceFd, primaryFbId);
            LocalCleanup();
            throw new Exception("Failed to set plane");
        }

        void LocalCleanup()
        {
            _primaryFrontBuffer?.Dispose();
            _primaryBackBuffer?.Dispose();
        }
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
            _primaryBackBuffer.FramebufferId,
            _primaryBackBuffer.Width,
            _primaryBackBuffer.Height);

        if (success)
        {
            // Swap buffers
            (_primaryFrontBuffer, _primaryBackBuffer) = (_primaryBackBuffer, _primaryFrontBuffer);
        }

        return success;
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

    public override void Cleanup()
    {
        base.Cleanup();
        if (_primaryFrontBuffer.FramebufferId != 0)
        {
            LibDrm.drmModeRmFB(_drmDevice.DeviceFd, _primaryFrontBuffer.FramebufferId);
        }

        _primaryFrontBuffer.DmaBuffer.UnmapBuffer();
        _primaryFrontBuffer.Dispose();

        if (_primaryBackBuffer.FramebufferId != 0)
        {
            LibDrm.drmModeRmFB(_drmDevice.DeviceFd, _primaryBackBuffer.FramebufferId);
        }

        _primaryBackBuffer.DmaBuffer.UnmapBuffer();
        _primaryBackBuffer.Dispose();

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
}