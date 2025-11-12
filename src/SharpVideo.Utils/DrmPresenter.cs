using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Gbm;

namespace SharpVideo.Utils;

/// <summary>
/// Universal DRM presenter wrapper that provides a simplified, type-erased API 
/// for common DRM presentation scenarios.
/// </summary>
/// <remarks>
/// This wrapper provides convenience factory methods that return a common interface,
/// hiding the generic type complexity of the underlying DrmPresenter&lt;TPrimary, TOverlay&gt;.
/// 
/// For PoC scenarios: Use this class when you need a simple API and don't need compile-time
/// type safety for the specific presenter types.
/// 
/// For production: Consider using DrmPresenter&lt;TPrimary, TOverlay&gt; directly for better
/// type safety and compile-time checking.
/// 
/// Thread Safety: Same as underlying DrmPresenter&lt;T, T&gt; - not thread-safe except for disposal.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class DrmPresenter : IDisposable
{
    private readonly IDisposable _innerPresenter;
    private readonly DrmSinglePlanePresenter _primaryPlanePresenter;
    private readonly DrmPlaneLastDmaBufferPresenter? _overlayPlanePresenter;
    private readonly DrmPlane _primaryPlane;
    private readonly DrmPlane? _overlayPlane;
    private readonly ILogger _logger;
    private bool _disposed;

    private DrmPresenter(
        IDisposable innerPresenter,
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
    /// Gets the primary DRM plane.
    /// </summary>
    public DrmPlane PrimaryPlane 
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _primaryPlane;
        }
    }

    /// <summary>
    /// Gets the overlay DRM plane.
    /// </summary>
    /// <exception cref="InvalidOperationException">No overlay plane configured</exception>
    public DrmPlane OverlayPlane 
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _overlayPlane ?? throw new InvalidOperationException("No overlay plane configured");
        }
    }

    /// <summary>
    /// Gets the primary plane presenter.
    /// Works with any presenter type: DMA, GBM, or GBM Atomic.
    /// </summary>
    public DrmSinglePlanePresenter PrimaryPlanePresenter
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _primaryPlanePresenter;
        }
    }

    /// <summary>
    /// Gets the overlay plane presenter.
    /// </summary>
    /// <exception cref="InvalidOperationException">No overlay plane configured</exception>
    public DrmPlaneLastDmaBufferPresenter OverlayPlanePresenter
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _overlayPlanePresenter ?? throw new InvalidOperationException("No overlay plane configured");
        }
    }

    /// <summary>
    /// Attempts to get the primary plane presenter as a DMA buffer presenter.
    /// </summary>
    /// <returns>The presenter if it's a DMA buffer presenter, null otherwise</returns>
    public DrmPlaneDoubleBufferPresenter? AsDmaBufferPresenter()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _primaryPlanePresenter as DrmPlaneDoubleBufferPresenter;
    }

    /// <summary>
    /// Attempts to get the primary plane presenter as a GBM presenter.
    /// </summary>
    /// <returns>The presenter if it's a GBM presenter, null otherwise</returns>
    public DrmPlaneGbmPresenter? AsGbmPresenter()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _primaryPlanePresenter as DrmPlaneGbmPresenter;
    }

    /// <summary>
    /// Attempts to get the primary plane presenter as a GBM Atomic presenter.
    /// </summary>
    /// <returns>The presenter if it's a GBM Atomic presenter, null otherwise</returns>
    public DrmPlaneGbmAtomicPresenter? AsGbmAtomicPresenter()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _primaryPlanePresenter as DrmPlaneGbmAtomicPresenter;
    }

    /// <summary>
    /// Creates a DRM presenter with DMA buffers for both primary and overlay planes.
    /// </summary>
    /// <remarks>
    /// This is the legacy method for backward compatibility with existing demos.
    /// Use case: CPU-rendered content on both planes.
    /// </remarks>
    /// <exception cref="DrmResourceNotFoundException">Required DRM resources not found</exception>
    /// <exception cref="DrmPlaneNotFoundException">Required plane not found</exception>
    public static DrmPresenter Create(
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
    /// Uses legacy API with blocking page flips.
    /// No overlay plane configured.
    /// </summary>
    /// <remarks>
    /// Use case: OpenGL ES rendering with blocking vsync for simple applications.
    /// </remarks>
    /// <exception cref="DrmResourceNotFoundException">Required DRM resources not found</exception>
    /// <exception cref="DrmPlaneNotFoundException">Required plane not found</exception>
    public static DrmPresenter CreateWithGbm(
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
    /// Uses atomic modesetting with non-blocking page flips and separate page flip thread.
    /// No overlay plane configured.
    /// </summary>
    /// <remarks>
    /// Use case: High-performance OpenGL ES rendering at maximum FPS without vsync blocking.
    /// Ideal for: ImGui-only applications, UI rendering, games.
    /// </remarks>
    /// <exception cref="DrmResourceNotFoundException">Required DRM resources not found</exception>
    /// <exception cref="DrmPlaneNotFoundException">Required plane not found</exception>
    public static DrmPresenter CreateWithGbmAtomic(
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
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ideal combination for applications with GPU-rendered UI and hardware-decoded video:
    /// - Primary plane: GBM atomic for high-performance OpenGL ES rendering (ImGui, UI)
    /// - Overlay plane: DMA buffers for zero-copy hardware-decoded video
    /// </para>
    /// <para>
    /// Architecture:
    /// - Overlay uses legacy SetPlane (not atomic) to avoid dual event loop conflict
    /// - GBM atomic presenter has dedicated event loop thread for page flip events
    /// - Primary and overlay rendering can occur in separate threads
    /// </para>
    /// <para>
    /// Thread Safety:
    /// - Primary plane: OpenGL ES context must be used from single render thread
    /// - Overlay plane: Can be updated from video decoder thread (different thread)
    /// </para>
    /// </remarks>
    /// <exception cref="DrmResourceNotFoundException">Required DRM resources not found</exception>
    /// <exception cref="DrmPlaneNotFoundException">Required plane not found</exception>
    public static DrmPresenter CreateWithGbmAtomicAndDmaOverlay(
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

        return new DrmPresenter(
            innerPresenter,
            innerPresenter.PrimaryPlane,
            innerPresenter.PrimaryPlanePresenter,
            innerPresenter.OverlayPlane,
            innerPresenter.OverlayPlanePresenter,
            logger);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _logger.LogInformation("Disposing DRM presenter wrapper");

        try
        {
            _innerPresenter.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during DRM presenter wrapper disposal");
        }
        finally
        {
            _disposed = true;
        }
    }
}