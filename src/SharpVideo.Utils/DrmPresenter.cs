using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Gbm;

namespace SharpVideo.Utils;

/// <summary>
/// Universal DRM presenter wrapper that can work with different primary plane presenter types.
/// Provides a simplified API for common DRM presentation scenarios while supporting flexibility.
/// </summary>
[SupportedOSPlatform("linux")]
public class DrmPresenter
{
    private readonly object _innerPresenter;
    private readonly DrmSinglePlanePresenter _primaryPlanePresenter;
    private readonly DrmPlaneLastDmaBufferPresenter? _overlayPlanePresenter;
    private readonly DrmPlane? _primaryPlane;
    private readonly DrmPlane? _overlayPlane;
    private readonly ILogger _logger;

    private DrmPresenter(
        object innerPresenter,
        DrmPlane primaryPlane,
        DrmSinglePlanePresenter primaryPlanePresenter,
        DrmPlane? overlayPlane,
        DrmPlaneLastDmaBufferPresenter? overlayPlanePresenter,
        ILogger logger)
    {
        _innerPresenter = innerPresenter;
        _primaryPlane = primaryPlane;
        _primaryPlanePresenter = primaryPlanePresenter;
        _overlayPlane = overlayPlane;
        _overlayPlanePresenter = overlayPlanePresenter;
        _logger = logger;
    }

    /// <summary>
    /// Gets the primary DRM plane
    /// </summary>
    public DrmPlane PrimaryPlane => _primaryPlane ?? throw new InvalidOperationException("Primary plane not available");

    /// <summary>
    /// Gets the overlay DRM plane (if configured)
    /// </summary>
    public DrmPlane OverlayPlane => _overlayPlane ?? throw new InvalidOperationException("No overlay plane configured");

    /// <summary>
    /// Gets the primary plane presenter (works with any presenter type: DMA, GBM, GBM Atomic)
    /// </summary>
    public DrmSinglePlanePresenter PrimaryPlanePresenter => _primaryPlanePresenter;

    /// <summary>
    /// Gets the overlay plane presenter (if configured)
    /// </summary>
    public DrmPlaneLastDmaBufferPresenter OverlayPlanePresenter =>
        _overlayPlanePresenter ?? throw new InvalidOperationException("No overlay plane configured");

    /// <summary>
    /// Gets the primary plane presenter as DMA buffer presenter (if that's the type)
    /// </summary>
    public DrmPlaneDoubleBufferPresenter? AsDmaBufferPresenter =>
        _primaryPlanePresenter as DrmPlaneDoubleBufferPresenter;

    /// <summary>
    /// Gets the primary plane presenter as GBM presenter (if that's the type)
    /// </summary>
    public DrmPlaneGbmPresenter? AsGbmPresenter =>
        _primaryPlanePresenter as DrmPlaneGbmPresenter;

    /// <summary>
    /// Gets the primary plane presenter as GBM Atomic presenter (if that's the type)
    /// </summary>
    public DrmPlaneGbmAtomicPresenter? AsGbmAtomicPresenter =>
        _primaryPlanePresenter as DrmPlaneGbmAtomicPresenter;

    /// <summary>
    /// Creates a DRM presenter with DMA buffers for both primary and overlay planes.
    /// This is the legacy method for backward compatibility.
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

        if (innerPresenter == null)
            return null;

        return new DrmPresenter(
            innerPresenter,
            innerPresenter.PrimaryPlane,
            innerPresenter.PrimaryPlanePresenter,
            innerPresenter.OverlayPlane,
            innerPresenter.OverlayPlanePresenter,
            logger);
    }

    /// <summary>
    /// Creates a DRM presenter with GBM-based primary plane for OpenGL ES rendering.
    /// No overlay plane configured.
    /// </summary>
    public static DrmPresenter? CreateWithGbm(
        DrmDevice drmDevice,
        uint width,
        uint height,
        GbmDevice gbmDevice,
        PixelFormat primaryPlanePixelFormat,
        ILogger logger)
    {
        var innerPresenter = DrmPresenter<DrmPlaneGbmPresenter, object>
            .CreateWithGbmBuffers<object>(
                drmDevice,
                width,
                height,
                gbmDevice,
                primaryPlanePixelFormat,
                logger);

        if (innerPresenter == null)
            return null;

        return new DrmPresenter(
            innerPresenter,
            innerPresenter.PrimaryPlane,
            innerPresenter.PrimaryPlanePresenter,
            null,
            null,
            logger);
    }

    /// <summary>
    /// Creates a DRM presenter with atomic GBM-based primary plane for high-performance OpenGL ES rendering.
    /// No overlay plane configured.
    /// </summary>
    public static DrmPresenter? CreateWithGbmAtomic(
        DrmDevice drmDevice,
        uint width,
        uint height,
        GbmDevice gbmDevice,
        PixelFormat primaryPlanePixelFormat,
        ILogger logger)
    {
        var innerPresenter = DrmPresenter<DrmPlaneGbmAtomicPresenter, object>
            .CreateWithGbmBuffersAtomic<object>(
                drmDevice,
                width,
                height,
                gbmDevice,
                primaryPlanePixelFormat,
                logger);

        if (innerPresenter == null)
            return null;

        return new DrmPresenter(
            innerPresenter,
            innerPresenter.PrimaryPlane,
            innerPresenter.PrimaryPlanePresenter,
            null,
            null,
            logger);
    }

    /// <summary>
    /// Creates a DRM presenter with atomic GBM primary plane and DMA buffer overlay plane.
    /// Ideal for applications with GPU-rendered UI (ImGui) and hardware-decoded video.
    /// Primary plane: GBM atomic for OpenGL ES rendering
    /// Overlay plane: DMA buffers for zero-copy video
    /// </summary>
    public static DrmPresenter? CreateWithGbmAtomicAndDmaOverlay(
        DrmDevice drmDevice,
        uint width,
        uint height,
        GbmDevice gbmDevice,
        DrmBufferManager bufferManager,
        PixelFormat primaryPlanePixelFormat,
        PixelFormat overlayPlanePixelFormat,
        ILogger logger)
    {
        var innerPresenter = DrmPresenter<DrmPlaneGbmAtomicPresenter, DrmPlaneLastDmaBufferPresenter>
            .CreateWithGbmAtomicAndDmaOverlay(
                drmDevice,
                width,
                height,
                gbmDevice,
                bufferManager,
                primaryPlanePixelFormat,
                overlayPlanePixelFormat,
                logger);

        if (innerPresenter == null)
            return null;

        return new DrmPresenter(
            innerPresenter,
            innerPresenter.PrimaryPlane,
            innerPresenter.PrimaryPlanePresenter,
            innerPresenter.OverlayPlane,
            innerPresenter.OverlayPlanePresenter,
            logger);
    }

    public void CleanupDisplay()
    {
        _logger.LogInformation("Cleaning up DRM presenter");

        try
        {
            _primaryPlanePresenter.Cleanup();
            _overlayPlanePresenter?.Cleanup();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during display cleanup");
        }
    }
}