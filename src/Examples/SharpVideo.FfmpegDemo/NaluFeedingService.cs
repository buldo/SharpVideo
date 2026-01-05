using Microsoft.Extensions.Logging;
using SharpVideo.Decoding;
using SharpVideo.Decoding.Ffmpeg;
using SharpVideo.FfmpegDemo.NaluSources;
using SharpVideo.H264;

namespace SharpVideo.FfmpegDemo;

/// <summary>
/// Service that reads NALUs from a source and feeds them to the decoder
/// </summary>
internal class NaluFeedingService : IDisposable
{
    private readonly StreamNaluSource _naluSource;
    private readonly FfmpegH264Decoder _decoder;
    private readonly ILogger<NaluFeedingService> _logger;
    private Task? _feedingTask;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public NaluFeedingService(
        StreamNaluSource naluSource,
        FfmpegH264Decoder decoder,
        ILogger<NaluFeedingService> logger)
    {
        _naluSource = naluSource ?? throw new ArgumentNullException(nameof(naluSource));
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Start()
    {
        if (_feedingTask != null)
        {
            throw new InvalidOperationException("NaluFeedingService already started");
        }

        _logger.LogInformation("Starting NALU feeding service");
        _cts = new CancellationTokenSource();
        _feedingTask = Task.Run(() => FeedingLoop(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts == null || _feedingTask == null)
        {
            return;
        }

        _logger.LogInformation("Stopping NALU feeding service");
        _cts.Cancel();

        try
        {
            await _feedingTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        _logger.LogInformation("NALU feeding service stopped");
    }

    private void FeedingLoop(CancellationToken cancellationToken)
    {
        int naluCount = 0;

        try
        {
            _logger.LogInformation("Entering NALU feeding loop");

            foreach (var nalu in _naluSource.NaluQueue.GetConsumingEnumerable(cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Cancellation requested in feeding loop");
                    break;
                }

                _logger.LogTrace("Processing NALU #{Count}", naluCount + 1);

                // Get a free buffer from the decoder
                ManagedMemoryEncodedBuffer? buffer = null;
                int retryCount = 0;
                while (buffer == null && !cancellationToken.IsCancellationRequested)
                {
                    buffer = _decoder.GetEncodedBuffersForReuse() as ManagedMemoryEncodedBuffer;
                    if (buffer == null)
                    {
                        retryCount++;
                        if (retryCount % 100 == 0)
                        {
                            _logger.LogWarning("Waiting for free buffer, retry count: {RetryCount}", retryCount);
                        }
                        // No free buffers available, wait a bit
                        Thread.Sleep(1);
                    }
                    else
                    {
                        if (retryCount > 0)
                        {
                            _logger.LogDebug("Got free buffer after {RetryCount} retries", retryCount);
                        }
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Cancellation requested after waiting for buffer");
                    break;
                }

                // Copy NALU data WITH start codes (Annex-B format required by FFmpeg)
                _logger.LogTrace("Writing NALU data to buffer ({Size} bytes)", nalu.Data.Length);
                buffer!.CopyFromNalu(nalu);

                // Send buffer to decoder
                _logger.LogTrace("Sending buffer to decoder (NALU #{Count})", naluCount + 1);
                _decoder.AddBufferForDecode(buffer);

                naluCount++;
                _logger.LogTrace("Successfully fed NALU #{Count}", naluCount);

                if (_logger.IsEnabled(LogLevel.Debug) && naluCount % 10 == 0)
                {
                    _logger.LogDebug("Fed {Count} NALUs so far", naluCount);
                }
            }

            _logger.LogInformation("Completed feeding NALUs: {Count} total", naluCount);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("NALU feeding cancelled after {Count} NALUs", naluCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in NALU feeding loop after {Count} NALUs", naluCount);
        }
        finally
        {
            _logger.LogInformation("Exiting NALU feeding loop");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
