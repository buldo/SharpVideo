using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Decoding.V4l2.Stateless;
using SharpVideo.Utils;
using SharpVideo.V4L2Decoding.NaluSources;
using SharpVideo.V4L2Decoding.Services;

namespace SharpVideo.V4L2DecodeDrmPreviewDemo2;

[SupportedOSPlatform("linux")]
public class Player
{
    private readonly DrmPresenter _presenter;
    private readonly V4l2H264StatelessDecoder _decoder;
    private readonly ILogger<Player> _logger;
    private readonly ILoggerFactory _loggerFactory;
    // Use bounded capacity to limit latency - max 3 frames in display queue
    private readonly BlockingCollection<SharedDmaBuffer> _buffersToPresent = new(boundedCapacity: 3);
    private readonly CancellationTokenSource displayCts = new CancellationTokenSource();

    private Task _decodeTask;
    private Task _displayTask;

    private ManualResetEventSlim _decodeCompleted = new(false);

    public Player(
        DrmPresenter presenter,
        V4l2H264StatelessDecoder decoder,
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
        _decodeTask = Task.Run(() => DecodeLocalAsync(fileStream));
        _displayTask = Task.Run(() => DisplayRoutine(displayCts.Token));
    }

    public void WaitCompleted()
    {
        _decodeCompleted.Wait();
        displayCts.Cancel(false);
        Task.WaitAll(_decodeTask, _displayTask);
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
        _decodeCompleted.Set();
    }

    private void DisplayRoutine(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Display thread started");
        var displayStopwatch = Stopwatch.StartNew();
        while(!(cancellationToken.IsCancellationRequested && Statistics.DecodedFrames == Statistics.PresentedFrames))
        {
            SharedDmaBuffer buffer;
            try
            {
                buffer = _buffersToPresent.Take(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

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
                // TODO
                //_decoder.ReuseDecodedFrame(toRequeue[i]);
            }
        }
        displayStopwatch.Stop();
        Statistics.PresentElapsed = displayStopwatch.Elapsed;

        _logger.LogInformation("Display thread stopped. Frames: {FrameCount}; Time: {Elapsed}s)",
            Statistics.PresentedFrames, displayStopwatch.Elapsed.TotalSeconds);
    }

}