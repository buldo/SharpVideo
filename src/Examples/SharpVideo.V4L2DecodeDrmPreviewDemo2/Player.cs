using System.Diagnostics;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Decoding;
using SharpVideo.Utils;
using SharpVideo.V4L2Decoding.NaluSources;

namespace SharpVideo.V4L2DecodeDrmPreviewDemo2;

/// <summary>
/// Optimized video player with minimal latency pipeline using DualPlanePresenter2.
/// Uses a simplified 2-thread model:
/// - Decode thread: reads NALUs and submits to decoder
/// - Display thread: receives decoded frames directly from BlockingCollection and presents them
/// </summary>
/// <remarks>
/// Latency optimizations:
/// 1. Direct read from decoder's BlockingCollection - no intermediate Channel
/// 2. Reduced NALU source buffer size for faster initial frame display
/// 3. Single display thread handles both decode output and presentation
/// 4. Uses DualPlanePresenter2 with OUT_FENCE_PTR for precise buffer release timing
/// </remarks>
[SupportedOSPlatform("linux")]
public class Player
{
    private readonly DualPlanePresenter2 _presenter;
    private readonly BaseDecoder<SharedDmaBuffer> _decoder;
    private readonly ILogger<Player> _logger;
    private readonly ILoggerFactory _loggerFactory;

    // Pre-allocated buffer for requeue operations to avoid allocations in hot path
    private readonly SharedDmaBuffer[] _requeueBuffer = new SharedDmaBuffer[4];

    private readonly CancellationTokenSource _cts = new();

    private Task? _decodeTask;
    private Task? _displayTask;

    private readonly ManualResetEventSlim _decodeCompleted = new(false);

    public Player(
        DualPlanePresenter2 presenter,
        BaseDecoder<SharedDmaBuffer> decoder,
        ILoggerFactory loggerFactory)
    {
        _presenter = presenter;
        _decoder = decoder;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<Player>();
    }

    public PlayerStatistics Statistics { get; } = new();

    public void Init()
    {
        _decoder.Initialize();
    }

    public void StartPlay(FileStream fileStream)
    {
        // Start the presenter commit thread
        _presenter.Start();

        // Start unified decode-display thread - reads from decoder and presents directly
        _displayTask = Task.Run(() => DecodeAndDisplayRoutine(_cts.Token));

        // Start decoding (feeds NALUs to decoder)
        _decodeTask = Task.Run(() => DecodeLocalAsync(fileStream));
    }

    public void WaitCompleted()
    {
        // Wait for decoding to finish
        _decodeCompleted.Wait();

        // Give display thread time to process remaining frames
        Thread.Sleep(100);

        // Signal cancellation
        _cts.Cancel();

        // Wait for all tasks to finish
        try
        {
            Task.WaitAll([_decodeTask!, _displayTask!], TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Expected when cancelled
        }

        // Stop the presenter
        _presenter.Stop();

        // Drain any remaining released buffers
        var releasedCount = _presenter.GetReleasedVideoBuffers(_requeueBuffer);
        for (int i = 0; i < releasedCount; i++)
        {
            _decoder.ReuseDecodedFrame(_requeueBuffer[i]);
        }
    }

    private async Task DecodeLocalAsync(FileStream fileStream)
    {
        // Use smaller NALU queue for reduced latency (optimized: 8 for more aggressive backpressure)
        await using var naluSource = new StreamNaluSource(
            fileStream,
            _loggerFactory.CreateLogger<StreamNaluSource>(),
            queueCapacity: 8);
        await naluSource.StartAsync();

        var queue = naluSource.NaluQueue;

        // Process all NALUs from the source
        foreach (var nalu in queue.GetConsumingEnumerable())
        {
            _decoder.Decode(nalu.Data);
            await Task.Delay(15);
        }

        _logger.LogInformation("All NALUs processed, signaling decode complete");
        _decodeCompleted.Set();
    }

    /// <summary>
    /// Unified decode output and display routine.
    /// Eliminates the intermediate Channel by reading directly from decoder's BlockingCollection.
    /// </summary>
    private void DecodeAndDisplayRoutine(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Unified decode-display thread started");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                SharedDmaBuffer decodedFrame;
                try
                {
                    // This blocks until a frame is available - direct from decoder
                    decodedFrame = _decoder.WaitForDecodedFrames();
                }
                catch (InvalidOperationException)
                {
                    // BlockingCollection was completed
                    break;
                }

                Statistics.IncrementDecodedFrames();

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("Presenting frame {FrameNumber}", Statistics.PresentedFrames + 1);
                }

                // Present using DualPlanePresenter2's EnqueueVideoFrame
                // Returns replaced buffer (if any) and drains release queue
                var (replacedBuffer, releasedCount) = _presenter.EnqueueVideoFrame(
                    decodedFrame,
                    _requeueBuffer);

                Statistics.IncrementPresentedFrames();

                // Handle replaced buffer (was pending but not yet committed)
                if (replacedBuffer != null)
                {
                    _decoder.ReuseDecodedFrame(replacedBuffer);
                }

                // Return released buffers to decoder for reuse
                for (int i = 0; i < releasedCount; i++)
                {
                    _decoder.ReuseDecodedFrame(_requeueBuffer[i]);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled
        }

        stopwatch.Stop();
        Statistics.DecodeElapsed = stopwatch.Elapsed;
        Statistics.PresentElapsed = stopwatch.Elapsed;

        _logger.LogInformation("Unified decode-display thread stopped. Frames: {FrameCount}; Time: {Elapsed}s)",
            Statistics.PresentedFrames, stopwatch.Elapsed.TotalSeconds);
    }
}