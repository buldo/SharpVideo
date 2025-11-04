using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;

namespace SharpVideo.Utils;

[SupportedOSPlatform("linux")]
public class DrmPlaneLastDmaBufferPresenter: DrmSinglePlanePresenter
{
    private readonly DrmBufferManager _bufferManager;
    private readonly AtomicFlipManager? _atomicDisplayManager;

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

        // Only use atomic mode if explicitly requested AND properties are available
        if (useAtomicMode)
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
        else
        {
            logger.LogInformation("Overlay plane configured to use legacy SetPlane mode (no atomic/event loop)");
        }
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

        var ret = _processedBuffers.ToArray();
        _processedBuffers.Clear();
        return ret;
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