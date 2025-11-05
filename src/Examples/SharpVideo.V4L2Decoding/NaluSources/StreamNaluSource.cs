using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SharpVideo.H264;

namespace SharpVideo.V4L2Decoding.NaluSources;

/// <summary>
/// Provides H.264 NAL units by reading from a stream (file or network stream).
/// Uses H264AnnexBNaluProvider internally to parse Annex-B format.
/// </summary>
public class StreamNaluSource : INaluSource
{
    private readonly Stream _stream;
    private readonly ILogger<StreamNaluSource>? _logger;
    private readonly BlockingCollection<H264Nalu> _naluQueue;
    private H264AnnexBNaluProvider? _naluProvider;
    private Task? _feedTask;
    private Task? _readTask;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public StreamNaluSource(Stream stream, ILogger<StreamNaluSource>? logger = null, int queueCapacity = 100)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _logger = logger;

        // Bounded collection for flow control
        _naluQueue = new BlockingCollection<H264Nalu>(queueCapacity);
    }

    public BlockingCollection<H264Nalu> NaluQueue => _naluQueue;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_naluProvider != null)
        {
            throw new InvalidOperationException("StreamNaluSource already started");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _naluProvider = new H264AnnexBNaluProvider();

        _logger?.LogInformation("Starting stream NALU source");

        // Start feeding stream data to NALU provider
        _feedTask = FeedStreamAsync(_cts.Token);
        _logger?.LogInformation("Feed task started");

        // Start reading NALUs and pushing to channel
        _readTask = ReadNalusAsync(_cts.Token);
        _logger?.LogInformation("Read task started");

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts == null || _naluProvider == null)
        {
            return;
        }

        _logger?.LogInformation("Stopping stream NALU source");

        _cts.Cancel();

        try
        {
            if (_feedTask != null)
            {
                await _feedTask;
            }

            if (_readTask != null)
            {
                await _readTask;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelling
        }

        // Channel is completed in ReadNalusAsync finally block
        _logger?.LogInformation("Stream NALU source stopped");
    }

    private async Task FeedStreamAsync(CancellationToken cancellationToken)
    {
        const int bufferSize = 16 * 1024; // 16KB chunks for low latency
        var buffer = new byte[bufferSize];
        long totalBytesRead = 0;

        try
        {
            int bytesRead;
            while ((bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await _naluProvider!.AppendData(buffer.AsSpan(0, bytesRead).ToArray(), cancellationToken);
                totalBytesRead += bytesRead;
            }

            _logger?.LogDebug("Completed feeding stream: {Bytes} bytes total", totalBytesRead);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("Stream feeding cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error feeding stream to NALU provider");
            throw;
        }
        finally
        {
            _naluProvider?.CompleteWriting();
        }
    }

    private async Task ReadNalusAsync(CancellationToken cancellationToken)
    {
        int naluCount = 0;

        try
        {
            await foreach (var nalu in _naluProvider!.NaluReader.ReadAllAsync(cancellationToken))
            {
                _naluQueue.Add(nalu, cancellationToken);
                naluCount++;

                if (_logger != null && _logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("Read NALU #{Index} ({Size} bytes)", naluCount, nalu.Data.Length);
                }
            }

            _logger?.LogInformation("Completed reading NALUs: {Count} total", naluCount);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("NALU reading cancelled after {Count} NALUs", naluCount);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error reading NALUs from provider");
            throw;
        }
        finally
        {
            // Signal that no more NALUs will be added
            _naluQueue.CompleteAdding();
            _logger?.LogDebug("Queue completed after reading {Count} NALUs", naluCount);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await StopAsync();

        _naluProvider?.Dispose();
        _cts?.Dispose();
        _naluQueue?.Dispose();

        if (_stream != null)
        {
            await _stream.DisposeAsync();
        }
    }
}
