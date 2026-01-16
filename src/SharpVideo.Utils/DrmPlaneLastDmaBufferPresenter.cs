using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;

namespace SharpVideo.Utils;

[SupportedOSPlatform("linux")]
public class DrmPlaneLastDmaBufferPresenter: DrmSinglePlanePresenter
{
    private readonly DrmBufferManager _bufferManager;
    private readonly AtomicFlipManager? _atomicDisplayManager;
    private readonly ILogger _logger;

    private readonly List<SharedDmaBuffer> _processedBuffers = new();

    private SharedDmaBuffer? _currentFrame;

    public DrmPlaneLastDmaBufferPresenter(
        DrmDevice drmDevice,
        DrmPlane plane,
        uint crtcId,
        uint width,
        uint height,
        DrmBufferManager bufferManager,
        ILogger logger,
        bool useAtomicMode = true)
        : base(drmDevice, plane, crtcId, width, height, logger)
    {
        _bufferManager = bufferManager;
        _logger = logger;

        // Only use atomic mode if explicitly requested AND capability is available
        if (useAtomicMode)
        {
            // Check if atomic mode is supported
            if (!drmDevice.TrySetClientCapability(DrmClientCapability.DRM_CLIENT_CAP_ATOMIC, true, out var result))
            {
                logger.LogWarning(
                    "Atomic mode not supported by DRM device (error code: {ErrorCode}), falling back to legacy mode",
                    result);
                useAtomicMode = false;
            }
            else
            {
                var props = new AtomicPlaneProperties(plane);
                if (props.IsValid())
                {
                    _atomicDisplayManager = new AtomicFlipManager(
                        drmDevice,
                        plane,
                        crtcId,
                        props,
                        width,
                        height,
                        width,
                        height,
                        logger);

                    logger.LogInformation("Overlay plane using atomic mode with dedicated event loop");
                }
                else
                {
                    logger.LogWarning("Atomic properties not available for overlay plane, using legacy mode");
                }
            }
        }

        if (_atomicDisplayManager == null)
        {
            logger.LogInformation("Overlay plane configured to use legacy SetPlane mode (no atomic/event loop)");
        }
    }

    public bool SetOverlayPlaneBuffer(SharedDmaBuffer drmBuffer)
    {
        if (drmBuffer.FramebufferId == 0)
        {
            _logger.LogTrace(
                "Creating framebuffer for buffer: DmaFd={DmaFd}, Hash={Hash}",
                drmBuffer.DmaBuffer.Fd, drmBuffer.GetHashCode());
            drmBuffer.FramebufferId = _bufferManager.CreateFramebuffer(drmBuffer);
        }
        else
        {
            _logger.LogTrace(
                "Reusing existing framebuffer: FbId={FbId}, DmaFd={DmaFd}, Hash={Hash}",
                drmBuffer.FramebufferId, drmBuffer.DmaBuffer.Fd, drmBuffer.GetHashCode());
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
            drmBuffer.FramebufferId,
            drmBuffer.Width,
            drmBuffer.Height);
    }

    public SharedDmaBuffer[] GetPresentedOverlayBuffers()
    {
        if (_atomicDisplayManager != null)
        {
            return _atomicDisplayManager.GetCompletedBuffers();
        }

        if (_processedBuffers.Count == 0)
        {
            return [];
        }
        var ret = _processedBuffers.ToArray();
        _processedBuffers.Clear();
        return ret;
    }

    /// <summary>
    /// Copy completed buffers to the destination span. Returns the count of buffers copied.
    /// More efficient than GetPresentedOverlayBuffers() as it avoids allocations.
    /// </summary>
    public int GetPresentedOverlayBuffers(Span<SharedDmaBuffer> destination)
    {
        if (_atomicDisplayManager != null)
        {
            return _atomicDisplayManager.GetCompletedBuffers(destination);
        }

        int count = Math.Min(_processedBuffers.Count, destination.Length);
        for (int i = 0; i < count; i++)
        {
            destination[i] = _processedBuffers[i];
        }
        _processedBuffers.RemoveRange(0, count);
        return count;
    }

    public override void Cleanup()
    {
        base.Cleanup();
        if (_atomicDisplayManager != null)
        {
            _atomicDisplayManager.Dispose();
        }
    }
}