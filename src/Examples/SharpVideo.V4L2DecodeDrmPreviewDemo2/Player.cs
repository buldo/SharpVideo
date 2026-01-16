using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using SharpVideo.Decoding;
using SharpVideo.Utils;
using SharpVideo.V4L2Decoding.NaluSources;

namespace SharpVideo.V4L2DecodeDrmPreviewDemo2;

/// <summary>
/// Optimized video player with minimal latency pipeline.
/// Uses a simplified 2-thread model:
/// - Decode thread: reads NALUs and submits to decoder
/// - Display thread: receives decoded frames directly and presents them
/// </summary>
/// <remarks>
/// Latency optimizations:
/// 1. Single-element channel between decoder and display (minimal buffering)
/// 2. Direct display from decoder output - no intermediate queue
/// 3. Reduced NALU source buffer size for faster initial frame display
/// </remarks>
[SupportedOSPlatform("linux")]
public class Player
{
    private readonly DrmPresenter _presenter;
    private readonly BaseDecoder<SharedDmaBuffer> _decoder;
    private readonly ILogger<Player> _logger;
    private readonly ILoggerFactory _loggerFactory;

    // Pre-allocated buffer for requeue operations to avoid allocations in hot path
    private readonly SharedDmaBuffer[] _requeueBuffer = new SharedDmaBuffer[4];

    // Use bounded channel with capacity 1 for minimal latency
    // This means we only buffer at most 1 frame between decode and display
    private readonly Channel<SharedDmaBuffer> _frameChannel = Channel.CreateBounded<SharedDmaBuffer>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

    private readonly CancellationTokenSource _cts = new();

    private Task? _decodeTask;
    private Task? _displayTask;
    private Task? _decoderOutputTask;

    private readonly ManualResetEventSlim _decodeCompleted = new(false);

    public Player(
        DrmPresenter presenter,
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
        // Start display thread first - it will wait for frames
        _displayTask = Task.Run(() => DisplayRoutineAsync(_cts.Token));

        // Start decoder output processor - bridges decoder blocking API to channel
        _decoderOutputTask = Task.Run(() => DecoderOutputRoutineAsync(_cts.Token));

        // Start decoding (feeds NALUs to decoder)
        _decodeTask = Task.Run(() => DecodeLocalAsync(fileStream));
    }

    public void WaitCompleted()
    {
        // Wait for decoding to finish
        _decodeCompleted.Wait();

        // Give decoder output thread time to process remaining frames
        // before cancelling
        Thread.Sleep(100);

        // Signal cancellation and complete the channel
        _cts.Cancel();
        _frameChannel.Writer.TryComplete();

        // Wait for all tasks to finish
        try
        {
            Task.WaitAll([_decodeTask!, _decoderOutputTask!, _displayTask!], TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Expected when cancelled
        }
    }

    private async Task DecodeLocalAsync(FileStream fileStream)
    {
        // Use smaller NALU queue for reduced latency (was 100, now 16)
        await using var naluSource = new StreamNaluSource(
            fileStream,
            _loggerFactory.CreateLogger<StreamNaluSource>(),
            queueCapacity: 16);
        await naluSource.StartAsync();

        var queue = naluSource.NaluQueue;

        // Process all NALUs from the source
        foreach (var nalu in queue.GetConsumingEnumerable())
        {
            _decoder.Decode(nalu.Data);
        }

        _logger.LogInformation("All NALUs processed, signaling decode complete");
        _decodeCompleted.Set();
    }

    private async Task DecoderOutputRoutineAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Decoder output thread started");
        var decodeStopwatch = Stopwatch.StartNew();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                SharedDmaBuffer decodedFrame;
                try
                {
                    // This blocks until a frame is available
                    decodedFrame = _decoder.WaitForDecodedFrames();
                }
                catch (InvalidOperationException)
                {
                    // BlockingCollection was completed
                    break;
                }

                Statistics.IncrementDecodedFrames();

                // Try synchronous write first to avoid async overhead
                // Channel capacity is 1, so this will succeed most of the time when display keeps up
                if (!_frameChannel.Writer.TryWrite(decodedFrame))
                {
                    await _frameChannel.Writer.WriteAsync(decodedFrame, cancellationToken);
                }

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("Frame decoded: {DecodedCount}", Statistics.DecodedFrames);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        finally
        {
            decodeStopwatch.Stop();
            Statistics.DecodeElapsed = decodeStopwatch.Elapsed;
            _frameChannel.Writer.TryComplete();
        }

        _logger.LogInformation("Decoder output thread stopped");
    }

    private async Task DisplayRoutineAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Display thread started");
        var displayStopwatch = Stopwatch.StartNew();

        try
        {
            await foreach (var buffer in _frameChannel.Reader.ReadAllAsync(cancellationToken))
            {
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("Presenting frame {FrameNumber}", Statistics.PresentedFrames + 1);
                }

                _presenter.OverlayPlanePresenter.SetOverlayPlaneBuffer(buffer);
                Statistics.IncrementPresentedFrames();

                // Use pre-allocated buffer to avoid allocations in hot path
                var requeueCount = _presenter.OverlayPlanePresenter.GetPresentedOverlayBuffers(_requeueBuffer);

                // Batch requeue for better performance
                for (int i = 0; i < requeueCount; i++)
                {
                    _decoder.ReuseDecodedFrame(_requeueBuffer[i]);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled
        }

        displayStopwatch.Stop();
        Statistics.PresentElapsed = displayStopwatch.Elapsed;

        _logger.LogInformation("Display thread stopped. Frames: {FrameCount}; Time: {Elapsed}s)",
            Statistics.PresentedFrames, displayStopwatch.Elapsed.TotalSeconds);
    }
}