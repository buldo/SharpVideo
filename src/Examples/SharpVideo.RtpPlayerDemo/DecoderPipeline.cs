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

        // Create RTP NALU source with bounded queue for low latency
        _naluSource = new RtpNaluSource(loggerFactory.CreateLogger<RtpNaluSource>(), queueCapacity: 30);

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
                if (_rtpReceiver.TryGetNalUnit(out var frameData, cancellationToken))
                {
                    // RTP receiver returns a complete frame with multiple NALUs,
                    // each prefixed with Annex-B start code (00 00 00 01)
                    // Split frame into individual NALUs and push each one
                    SplitAndPushNalus(frameData);
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

    /// <summary>
    /// Split Annex-B formatted frame into individual NALUs and push to source
    /// </summary>
    private void SplitAndPushNalus(byte[] frameData)
    {
        if (frameData == null || frameData.Length == 0)
        {
            return;
        }

        int nalusFound = 0;
        int pos = 0;

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Splitting frame data: {Size} bytes", frameData.Length);
        }

        while (pos < frameData.Length)
        {
            // Find start code
            int startCodeLength = 0;
            if (pos + 4 <= frameData.Length &&
                frameData[pos] == 0 && frameData[pos + 1] == 0 && 
                frameData[pos + 2] == 0 && frameData[pos + 3] == 1)
            {
                startCodeLength = 4;
            }
            else if (pos + 3 <= frameData.Length &&
                     frameData[pos] == 0 && frameData[pos + 1] == 0 && frameData[pos + 2] == 1)
            {
                startCodeLength = 3;
            }
            else
            {
                // No start code found, skip this byte
                pos++;
                continue;
            }

            // Find next start code to determine NALU length
            int nextPos = pos + startCodeLength;
            int naluEnd = frameData.Length;

            for (int i = nextPos; i < frameData.Length - 2; i++)
            {
                if ((i + 3 < frameData.Length && 
                     frameData[i] == 0 && frameData[i + 1] == 0 && frameData[i + 2] == 0 && frameData[i + 3] == 1) ||
                    (i + 2 < frameData.Length && 
                     frameData[i] == 0 && frameData[i + 1] == 0 && frameData[i + 2] == 1))
                {
                    naluEnd = i;
                    break;
                }
            }

            // Extract NALU (including start code)
            int naluLength = naluEnd - pos;
            if (naluLength > startCodeLength)
            {
                var nalu = frameData.AsSpan(pos, naluLength);
                
                // Get NALU type for logging (skip start code to read NAL header)
                byte nalHeader = frameData[pos + startCodeLength];
                int nalType = nalHeader & 0x1F;

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("Found NALU: type={Type}, size={Size} bytes", nalType, naluLength);
                }
                
                // Push NALU with existing start code
                if (_naluSource.PushNalu(nalu, ensureStartCode: false))
                {
                    nalusFound++;
                }
                else
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.LogWarning("Failed to push NALU type {Type}, queue full", nalType);
                    }
                }
            }

            pos = naluEnd;
        }

        if (_logger.IsEnabled(LogLevel.Debug) && nalusFound > 0)
        {
            _logger.LogDebug("Split frame into {Count} NALUs", nalusFound);
        }
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
