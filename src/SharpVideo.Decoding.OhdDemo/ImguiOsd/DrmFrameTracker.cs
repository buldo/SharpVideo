using Microsoft.Extensions.Logging;

using SharpVideo.DmaBuffers;
using SharpVideo.Utils;

namespace SharpVideo.Decoding.OhdDemo.ImguiOsd;

/// <summary>
/// Tracks video frames that are currently being displayed by DRM.
/// Handles the lifecycle of frames between the video renderer and DRM presenter.
/// </summary>
/// <remarks>
/// DRM presents frames asynchronously - when a frame is submitted to the overlay plane,
/// it may still be displayed for several vsync periods. This tracker ensures frames
/// are not returned to the decoder until DRM has finished displaying them.
/// </remarks>
/// <typeparam name="TFrame">The type of decoded frame being tracked.</typeparam>
internal sealed class DrmFrameTracker<TFrame> where TFrame : class
{
    private readonly DrmPlaneLastDmaBufferPresenter _overlayPresenter;
    private readonly Func<TFrame, SharedDmaBuffer> _getDmaBuffer;
    private readonly Action<TFrame> _releaseFrame;
    private readonly ILogger _logger;
    private readonly Dictionary<SharedDmaBuffer, TFrame> _framesInUse = new();
    private readonly object _lock = new();

    /// <summary>
    /// Creates a new frame tracker.
    /// </summary>
    /// <param name="overlayPresenter">The DRM overlay plane presenter.</param>
    /// <param name="getDmaBuffer">Function to get the DMA buffer from a frame.</param>
    /// <param name="releaseFrame">Action to release a frame back to the decoder.</param>
    /// <param name="logger">Logger instance.</param>
    public DrmFrameTracker(
        DrmPlaneLastDmaBufferPresenter overlayPresenter,
        Func<TFrame, SharedDmaBuffer> getDmaBuffer,
        Action<TFrame> releaseFrame,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(overlayPresenter);
        ArgumentNullException.ThrowIfNull(getDmaBuffer);
        ArgumentNullException.ThrowIfNull(releaseFrame);
        ArgumentNullException.ThrowIfNull(logger);

        _overlayPresenter = overlayPresenter;
        _getDmaBuffer = getDmaBuffer;
        _releaseFrame = releaseFrame;
        _logger = logger;
    }

    /// <summary>
    /// Tracks a frame that is being submitted to DRM for display.
    /// </summary>
    public void TrackFrame(TFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var dmaBuffer = _getDmaBuffer(frame);

        lock (_lock)
        {
            _framesInUse[dmaBuffer] = frame;
        }

        _logger.LogTrace("Frame tracked, total in use: {Count}", _framesInUse.Count);
    }

    /// <summary>
    /// Releases frames that DRM has finished displaying.
    /// Should be called periodically from the video render loop.
    /// </summary>
    public void ReleaseCompletedFrames()
    {
        var completedBuffers = _overlayPresenter.GetPresentedOverlayBuffers();

        if (completedBuffers.Length == 0)
        {
            return;
        }

        lock (_lock)
        {
            foreach (var buffer in completedBuffers)
            {
                if (_framesInUse.Remove(buffer, out var frameToRelease))
                {
                    _releaseFrame(frameToRelease);
                    _logger.LogTrace("Released completed frame, remaining: {Count}", _framesInUse.Count);
                }
            }
        }
    }

    /// <summary>
    /// Releases all tracked frames. Call during cleanup.
    /// </summary>
    public void ReleaseAllFrames()
    {
        lock (_lock)
        {
            foreach (var frame in _framesInUse.Values)
            {
                _releaseFrame(frame);
            }

            var count = _framesInUse.Count;
            _framesInUse.Clear();

            if (count > 0)
            {
                _logger.LogDebug("Released {Count} remaining tracked frames", count);
            }
        }
    }

    /// <summary>
    /// Gets the number of frames currently being tracked.
    /// </summary>
    public int TrackedFrameCount
    {
        get
        {
            lock (_lock)
            {
                return _framesInUse.Count;
            }
        }
    }
}
