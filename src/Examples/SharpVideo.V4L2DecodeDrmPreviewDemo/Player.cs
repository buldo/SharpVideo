using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

using Microsoft.Extensions.Logging;

using SharpVideo.Drm;
using SharpVideo.Utils;
using SharpVideo.V4L2StatelessDecoder.Services;

namespace SharpVideo.V4L2DecodeDrmPreviewDemo;

[SupportedOSPlatform("linux")]
public class Player
{
    private readonly DrmPresenter _presenter;
    private readonly H264V4L2StatelessDecoder _decoder;
    private readonly ILogger<Player> _logger;
    // Use bounded capacity to limit latency - max 3 frames in display queue
    private readonly BlockingCollection<SharedDmaBuffer> _buffersToPresent = new(boundedCapacity: 3);
    private readonly CancellationTokenSource displayCts = new CancellationTokenSource();

    private Task _decodeTask;
    private Task _displayTask;

    private ManualResetEventSlim _decodeCompleted = new(false);

    public Player(
        DrmPresenter presenter,
        H264V4L2StatelessDecoder decoder,
        ILogger<Player> logger)
    {
        _presenter = presenter;
        _decoder = decoder;
        _logger = logger;
    }

    public PlayerStatistics Statistics { get; } = new();

    public void Init()
    {
        _decoder.InitializeDecoder(ProcessBuffer);
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
        Statistics.DecodeElapsed = _decoder.Statistics.DecodeElapsed;
    }

    private void ProcessBuffer(SharedDmaBuffer buffer)
    {
        Statistics.IncrementDecodedFrames();
        
        // Try to add without blocking - if queue is full, drop the oldest frame to reduce latency
        if (!_buffersToPresent.TryAdd(buffer, 0))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Display queue full, frame may be delayed");
            }
            _buffersToPresent.Add(buffer); // Block until space available
        }
        
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Frame decoded: {DecodedCount}", Statistics.DecodedFrames);
        }
    }

    private async Task DecodeLocalAsync(FileStream fileStream)
    {
        await _decoder.DecodeStreamAsync(fileStream);
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
    
            _presenter.SetOverlayPlane(buffer);
            Statistics.IncrementPresentedFrames();
            var toRequeue = _presenter.GetPresentedFrames();
    
            // Batch requeue for better performance
            for (int i = 0; i < toRequeue.Length; i++)
            {
                _decoder.RequeueCaptureBuffer(toRequeue[i]);
            }
        }
        displayStopwatch.Stop();

        _logger.LogInformation("Display thread stopped. Frames: {FrameCount}; Time: {Elapsed}s)", 
            Statistics.PresentedFrames, displayStopwatch.Elapsed.TotalSeconds);
    }

}