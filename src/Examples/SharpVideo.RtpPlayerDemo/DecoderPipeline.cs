using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Utils;
using SharpVideo.V4L2Decoding.Services;

namespace SharpVideo.RtpPlayerDemo;

/// <summary>
/// Manages H.264 decoding pipeline from RTP receiver to DRM display
/// </summary>
[SupportedOSPlatform("linux")]
public class DecoderPipeline : IAsyncDisposable
{
    private readonly RtpReceiverService _rtpReceiver;
    private readonly H264V4L2StatelessDecoder _decoder;
    private readonly DrmPresenter _presenter;
    private readonly ILogger<DecoderPipeline> _logger;
    private readonly BlockingCollection<SharedDmaBuffer> _buffersToPresent = new(boundedCapacity: 3);
    private readonly CancellationTokenSource _cts = new();
    
    private Task? _receiveTask;
    private Task? _displayTask;
    private readonly Stopwatch _decodeStopwatch = new();
    private readonly Stopwatch _presentStopwatch = new();
    
    public DecoderPipeline(
        RtpReceiverService rtpReceiver,
        H264V4L2StatelessDecoder decoder,
        DrmPresenter presenter,
        ILogger<DecoderPipeline> logger)
    {
        _rtpReceiver = rtpReceiver;
        _decoder = decoder;
        _presenter = presenter;
        _logger = logger;
        
        Statistics = new PlayerStatistics();
    }

    public PlayerStatistics Statistics { get; }

    /// <summary>
    /// Initialize decoder with buffer callback
    /// </summary>
    public void Initialize()
    {
        _decoder.InitializeDecoder(OnBufferDecoded);
        _logger.LogInformation("Decoder pipeline initialized");
    }

    /// <summary>
    /// Start decoding and display tasks
    /// </summary>
    public void Start()
    {
        _decodeStopwatch.Start();
        _presentStopwatch.Start();
        
        _receiveTask = Task.Run(() => ReceiveAndDecodeRoutine(_cts.Token));
        _displayTask = Task.Run(() => DisplayRoutine(_cts.Token));
        
        _logger.LogInformation("Decoder pipeline started");
    }

    /// <summary>
    /// Stop pipeline and wait for completion
    /// </summary>
    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping decoder pipeline...");
        
        _cts.Cancel();
        
        if (_receiveTask != null)
            await _receiveTask;
        if (_displayTask != null)
            await _displayTask;
        
        _decodeStopwatch.Stop();
        _presentStopwatch.Stop();
        
        Statistics.DecodeElapsed = _decodeStopwatch.Elapsed;
        Statistics.PresentElapsed = _presentStopwatch.Elapsed;
        
        _logger.LogInformation("Decoder pipeline stopped");
    }

    private void OnBufferDecoded(SharedDmaBuffer buffer)
    {
        Statistics.IncrementDecodedFrames();

        // Try to add without blocking - if queue is full, drop oldest frame
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

    private async Task ReceiveAndDecodeRoutine(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Receive and decode thread started");
        
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_rtpReceiver.TryGetNalUnit(out var nalUnit, cancellationToken))
                {
                    // Create memory stream from NAL unit
                    using var memoryStream = new MemoryStream(nalUnit);
                    
                    // Decode this NAL unit
                    await _decoder.DecodeStreamAsync(memoryStream);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Receive and decode routine cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in receive and decode routine");
        }
        
        _logger.LogInformation("Receive and decode thread stopped");
    }

    private void DisplayRoutine(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Display thread started");
        
        try
        {
            while (!cancellationToken.IsCancellationRequested)
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
                    _decoder.RequeueCaptureBuffer(toRequeue[i]);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in display routine");
        }

        _logger.LogInformation("Display thread stopped. Frames: {FrameCount}; Time: {Elapsed}s",
            Statistics.PresentedFrames, _presentStopwatch.Elapsed.TotalSeconds);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts.Dispose();
        _buffersToPresent.Dispose();
    }
}
