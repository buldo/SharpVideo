using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Decoding;
using SharpVideo.Utils;
using SharpVideo.V4L2Decoding.NaluSources;

namespace SharpVideo.V4L2DecodeDrmPreviewDemo2;

[SupportedOSPlatform("linux")]
public class Player
{
    private readonly DrmPresenter _presenter;
    private readonly BaseDecoder<SharedDmaBuffer> _decoder;
    private readonly ILogger<Player> _logger;
    private readonly ILoggerFactory _loggerFactory;
    // Use bounded capacity to limit latency - max 3 frames in display queue
    private readonly BlockingCollection<SharedDmaBuffer> _buffersToPresent = new(boundedCapacity: 3);
    private readonly CancellationTokenSource _displayCts = new();
    private readonly CancellationTokenSource _decoderOutputCts = new();

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
        // Start thread that receives decoded frames from decoder and queues for display
        _decoderOutputTask = Task.Run(() => DecoderOutputRoutine(_decoderOutputCts.Token));

        // Start decoding (feeds NALUs to decoder)
        _decodeTask = Task.Run(() => DecodeLocalAsync(fileStream));

        // Start display thread
        _displayTask = Task.Run(() => DisplayRoutine(_displayCts.Token));
    }

    public void WaitCompleted()
    {
        // Wait for decoding to finish
        _decodeCompleted.Wait();

        // Stop decoder output thread
        _decoderOutputCts.Cancel();
        _decoderOutputTask?.Wait();

        // Mark display queue as complete
        _buffersToPresent.CompleteAdding();

        // Wait for display to finish remaining frames
        _displayTask?.Wait();

        _decodeTask?.Wait();
    }

    private void ProcessBuffer(SharedDmaBuffer buffer)
    {
        Statistics.IncrementDecodedFrames();

        // Add to display queue - blocks if queue is full (back-pressure)
        _buffersToPresent.Add(buffer);

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Frame decoded: {DecodedCount}, queue size: {QueueSize}",
                Statistics.DecodedFrames, _buffersToPresent.Count);
        }
    }

    private async Task DecodeLocalAsync(FileStream fileStream)
    {
        await using var naluSource = new StreamNaluSource(fileStream, _loggerFactory.CreateLogger<StreamNaluSource>());
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

    private void DecoderOutputRoutine(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Decoder output thread started");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // This blocks until a frame is available
                var decodedFrame = _decoder.WaitForDecodedFrames();
                ProcessBuffer(decodedFrame);
            }
            catch (InvalidOperationException)
            {
                // BlockingCollection was completed
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Decoder output thread stopped");
    }

    private void DisplayRoutine(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Display thread started");
        var displayStopwatch = Stopwatch.StartNew();

        try
        {
            foreach (var buffer in _buffersToPresent.GetConsumingEnumerable(cancellationToken))
            {
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("Presenting frame {FrameNumber}; InQueue: {InQueue}",
                        Statistics.PresentedFrames + 1, _buffersToPresent.Count);
                }

                _presenter.OverlayPlanePresenter.SetOverlayPlaneBuffer(buffer);
                Statistics.IncrementPresentedFrames();
                var toRequeue = _presenter.OverlayPlanePresenter.GetPresentedOverlayBuffers();

                // Batch requeue for better performance
                for (int i = 0; i < toRequeue.Length; i++)
                {
                    _decoder.ReuseDecodedFrame(toRequeue[i]);
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