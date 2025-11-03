using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;

namespace SharpVideo.Utils;

/// <summary>
/// Legacy DRM presenter using DMA buffers for both primary and overlay planes.
/// This is a compatibility wrapper around DrmPresenter&lt;DrmPlaneDoubleBufferPresenter, DrmPlaneLastDmaBufferPresenter&gt;.
/// For new code, consider using the generic DrmPresenter&lt;TPrimaryPresenter, TOverlayPresenter&gt; directly.
/// </summary>
[SupportedOSPlatform("linux")]
public class DrmPresenter
{
    private readonly DrmPresenter<DrmPlaneDoubleBufferPresenter, DrmPlaneLastDmaBufferPresenter> _innerPresenter;

    private DrmPresenter(
        DrmPresenter<DrmPlaneDoubleBufferPresenter, DrmPlaneLastDmaBufferPresenter> innerPresenter)
    {
        _innerPresenter = innerPresenter;
    }

    public DrmPlane PrimaryPlane => _innerPresenter.PrimaryPlane;

    public DrmPlaneDoubleBufferPresenter PrimaryPlanePresenter => _innerPresenter.PrimaryPlanePresenter;

    public DrmPlane OverlayPlane => _innerPresenter.OverlayPlane ?? throw new InvalidOperationException("No overlay plane configured");

    public DrmPlaneLastDmaBufferPresenter OverlayPlanePresenter => _innerPresenter.OverlayPlanePresenter ?? throw new InvalidOperationException("No overlay plane configured");

    /// <summary>
    /// Creates a DRM presenter with DMA buffers for both primary and overlay planes.
    /// </summary>
    public static DrmPresenter? Create(
        DrmDevice drmDevice,
        uint width,
        uint height,
        DrmBufferManager bufferManager,
        PixelFormat primaryPlanePixelFormat,
        PixelFormat? overlayPlanePixelFormat,
        ILogger logger)
    {
        var innerPresenter = DrmPresenter<DrmPlaneDoubleBufferPresenter, DrmPlaneLastDmaBufferPresenter>
   .CreateWithDmaBuffers(
      drmDevice,
                width,
 height,
       bufferManager,
      primaryPlanePixelFormat,
       overlayPlanePixelFormat,
         logger);

        return innerPresenter != null ? new DrmPresenter(innerPresenter) : null;
    }

    public void CleanupDisplay()
    {
        _innerPresenter.CleanupDisplay();
    }
}