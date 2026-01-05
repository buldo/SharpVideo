using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Decoding.V4l2;
using SharpVideo.Decoding.V4l2.Stateless;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Utils;

namespace SharpVideo.Decoding.OhdDemo.ImguiOsd;

/// <summary>
/// Manages video frame rendering to DRM overlay plane on a dedicated thread.
/// </summary>
/// <remarks>
/// Runs independently from OSD rendering, displaying video frames as soon as
/// they are available from the decoder. Uses DrmFrameTracker to ensure frames
/// are not released until DRM has finished displaying them.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class DrmVideoRenderLoop : IDisposable
{
    /// <summary>
    /// Delay when no frame is available, in milliseconds.
    /// </summary>
    private const int NoFrameAvailableDelayMs = 1;

    /// <summary>
    /// Number of frames between debug log messages.
    /// </summary>
    private const int DebugLogInterval = 30;

    /// <summary>
    /// Timeout for thread join during disposal.
    /// </summary>
    private static readonly TimeSpan ThreadJoinTimeout = TimeSpan.FromSeconds(2);

    private readonly VideoPlaneRenderer _videoPlaneRenderer;
    private readonly DrmFrameTracker<V4l2DecodedFrame> _frameTracker;
    private readonly VideoFrameManager<V4l2H264StatelessDecoder, V4l2EncodedBuffer, V4l2DecodedFrame> _videoFrameManager;
    private readonly Action<V4l2DecodedFrame>? _onFrameRendered;
    private readonly ILogger _logger;

    private Thread? _renderThread;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <summary>
    /// Gets the total number of frames rendered since start.
    /// </summary>
    public int RenderedFrameCount { get; private set; }

    /// <summary>
    /// Creates a new video render loop.
    /// </summary>
    /// <param name="overlayPresenter">DRM overlay plane presenter.</param>
    /// <param name="bufferManager">DRM buffer manager.</param>
    /// <param name="videoPixelFormat">Pixel format for video frames.</param>
    /// <param name="videoFrameManager">Video frame manager for acquiring frames.</param>
    /// <param name="onFrameRendered">Optional callback invoked after each frame is rendered.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    public DrmVideoRenderLoop(
        DrmPlaneLastDmaBufferPresenter overlayPresenter,
        DrmBufferManager bufferManager,
        PixelFormat videoPixelFormat,
        VideoFrameManager<V4l2H264StatelessDecoder, V4l2EncodedBuffer, V4l2DecodedFrame> videoFrameManager,
        Action<V4l2DecodedFrame>? onFrameRendered,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(overlayPresenter);
        ArgumentNullException.ThrowIfNull(bufferManager);
        ArgumentNullException.ThrowIfNull(videoFrameManager);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _videoFrameManager = videoFrameManager;
        _onFrameRendered = onFrameRendered;
        _logger = loggerFactory.CreateLogger<DrmVideoRenderLoop>();

        _videoPlaneRenderer = new VideoPlaneRenderer(
            overlayPresenter,
            bufferManager,
            videoPixelFormat,
            loggerFactory.CreateLogger<VideoPlaneRenderer>());

        _frameTracker = new DrmFrameTracker<V4l2DecodedFrame>(
            overlayPresenter,
            frame => frame.DmaBuffer,
            frame => _videoFrameManager.ReleaseFrame(frame),
            _logger);
    }

    /// <summary>
    /// Starts the video render thread.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_renderThread != null)
        {
            throw new InvalidOperationException("Video render loop already started");
        }

        _cts = new CancellationTokenSource();
        _renderThread = new Thread(RenderThreadProc)
        {
            Name = "VideoRenderThread",
            IsBackground = true
        };
        _renderThread.Start();

        _logger.LogInformation("Video render loop started");
    }

    /// <summary>
    /// Stops the video render thread and releases resources.
    /// </summary>
    public void Stop()
    {
        if (_cts == null)
        {
            return;
        }

        _logger.LogInformation("Stopping video render loop...");
        _cts.Cancel();

        if (_renderThread is { IsAlive: true })
        {
            if (!_renderThread.Join(ThreadJoinTimeout))
            {
                _logger.LogWarning("Video render thread did not stop gracefully within {Timeout}s",
                    ThreadJoinTimeout.TotalSeconds);
            }
        }

        _cts.Dispose();
        _cts = null;
        _renderThread = null;

        // Release any frames still being tracked
        _frameTracker.ReleaseAllFrames();

        _logger.LogInformation("Video render loop stopped, rendered {Count} frames total", RenderedFrameCount);
    }

    private void RenderThreadProc()
    {
        _logger.LogInformation("Video render thread started");

        try
        {
            while (!_cts!.Token.IsCancellationRequested)
            {
                // Release buffers that DRM has finished displaying
                _frameTracker.ReleaseCompletedFrames();

                var frame = _videoFrameManager.AcquireCurrentFrame();
                if (frame == null)
                {
                    Thread.Sleep(NoFrameAvailableDelayMs);
                    continue;
                }

                RenderFrame(frame);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in video render thread after {Count} frames", RenderedFrameCount);
        }
        finally
        {
            _logger.LogInformation("Video render thread exited after {Count} frames", RenderedFrameCount);
        }
    }

    private void RenderFrame(V4l2DecodedFrame frame)
    {
        _logger.LogTrace("Rendering video frame #{Count} to overlay plane", RenderedFrameCount + 1);

        // Track frame before submitting to DRM
        _frameTracker.TrackFrame(frame);

        // Render to overlay plane
        _videoPlaneRenderer.RenderFrame(frame);
        RenderedFrameCount++;

        // Invoke callback if provided
        _onFrameRendered?.Invoke(frame);

        if (RenderedFrameCount % DebugLogInterval == 0)
        {
            _logger.LogDebug("Rendered {Count} video frames", RenderedFrameCount);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Stop();
        _videoPlaneRenderer.Dispose();
    }
}
