using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Utils;
using SharpVideo.V4L2Decoding.Services;
using SharpVideo.V4L2Decoding.NaluSources;

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
    private readonly RtpNaluSource _naluSource;

    private Task? _rtpFeedTask;
    private Task? _displayTask;
    private readonly Stopwatch _decodeStopwatch = new();
    private readonly Stopwatch _presentStopwatch = new();

    public DecoderPipeline(
        RtpReceiverService rtpReceiver,
        H264V4L2StatelessDecoder decoder,
        DrmPresenter presenter,
        ILoggerFactory loggerFactory)
    {
        _rtpReceiver = rtpReceiver;
        _decoder = decoder;
        _presenter = presenter;
        _logger = loggerFactory.CreateLogger<DecoderPipeline>();

        // Create RTP NALU source with bounded channel for low latency
        _naluSource = new RtpNaluSource(loggerFactory.CreateLogger<RtpNaluSource>(), channelCapacity: 30);

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
    public async Task StartAsync()
    {
        _decodeStopwatch.Start();
        _presentStopwatch.Start();

        // Start NALU source
        await _naluSource.StartAsync(_cts.Token);

        // Start decoder with NALU source
        _decoder.StartDecoding(_naluSource);

        // Start RTP feed task to push NALUs from RTP receiver to source
        _rtpFeedTask = Task.Run(() => RtpFeedRoutine(_cts.Token));

        // Start display task
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

        // Stop RTP feed
        if (_rtpFeedTask != null)
            await _rtpFeedTask;

        // Stop NALU source
        await _naluSource.StopAsync();

        // Stop decoder
        await _decoder.StopDecodingAsync();

        // Stop display
        if (_displayTask != null)
            await _displayTask;

        _decodeStopwatch.Stop();
        _presentStopwatch.Stop();

        Statistics.DecodeElapsed = _decoder.Statistics.DecodeElapsed;
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

    /// <summary>
    /// Feeds NAL units from RTP receiver to NALU source (minimal latency path)
    /// </summary>
    private void RtpFeedRoutine(CancellationToken cancellationToken)
    {
        _logger.LogInformation("RTP feed thread started");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_rtpReceiver.TryGetNalUnit(out var nalUnit, cancellationToken))
                {
                    // Push NAL unit directly to source (thread-safe, non-blocking)
                    // NAL units from RTP already have Annex-B start codes
                    _naluSource.PushNalu(nalUnit, ensureStartCode: false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("RTP feed routine cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in RTP feed routine");
        }

        _logger.LogInformation("RTP feed thread stopped");
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
        await _naluSource.DisposeAsync();
        _cts.Dispose();
        _buffersToPresent.Dispose();
    }
}
