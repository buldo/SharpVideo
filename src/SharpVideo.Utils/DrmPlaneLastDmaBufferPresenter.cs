using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;

namespace SharpVideo.Utils;

[SupportedOSPlatform("linux")]
public class DrmPlaneLastDmaBufferPresenter: DrmSinglePlanePresenter
{
    private readonly DrmBufferManager _bufferManager;
    private readonly AtomicDisplayManager? _atomicDisplayManager;

    private readonly List<SharedDmaBuffer> _processedBuffers = new();

    private SharedDmaBuffer? _currentFrame;

    public DrmPlaneLastDmaBufferPresenter(
        DrmDevice drmDevice,
        DrmPlane plane,
        uint crtcId,
        uint width,
        uint height,
        DrmCapabilitiesState capabilities,
        DrmBufferManager bufferManager,
        AtomicDisplayManager? atomicDisplayManager,
        AtomicPlaneUpdater? atomicUpdater,
        ILogger logger)
        : base(drmDevice, plane, crtcId, width, height, capabilities, atomicUpdater, logger)
    {
        _bufferManager = bufferManager;
        _atomicDisplayManager = atomicDisplayManager;
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