using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SharpVideo.H264;

namespace SharpVideo.V4L2Decoding.NaluSources;

/// <summary>
/// Provides H.264 NAL units received from RTP depacketizer.
/// Designed for minimal latency - NAL units are pushed directly to queue as they arrive.
/// </summary>
public class RtpNaluSource : INaluSource
{
    private readonly ILogger<RtpNaluSource>? _logger;
    private readonly BlockingCollection<H264Nalu> _naluQueue;
    private bool _disposed;
    private bool _started;

    public RtpNaluSource(ILogger<RtpNaluSource>? logger = null, int queueCapacity = 30)
    {
        _logger = logger;

        // Bounded collection to prevent memory overflow if decoder can't keep up
        _naluQueue = new BlockingCollection<H264Nalu>(queueCapacity);
    }

    public BlockingCollection<H264Nalu> NaluQueue => _naluQueue;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            throw new InvalidOperationException("RtpNaluSource already started");
        }

        _started = true;
        _logger?.LogInformation("RTP NALU source started");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!_started)
        {
            return Task.CompletedTask;
        }

        _logger?.LogInformation("Stopping RTP NALU source");
        _naluQueue.CompleteAdding();
        _started = false;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Push a NAL unit received from RTP to the decoder.
    /// This method is thread-safe and can be called from network receive thread.
    /// </summary>
    /// <param name="naluData">Complete NAL unit data (with or without Annex-B start code)</param>
    /// <param name="ensureStartCode">If true, adds Annex-B start code if not present</param>
    /// <returns>True if NAL unit was queued, false if channel is full and frame was dropped</returns>
    public bool PushNalu(ReadOnlySpan<byte> naluData, bool ensureStartCode = true)
    {
        if (!_started || _disposed)
        {
            return false;
        }

        if (naluData.Length == 0)
        {
            return false;
        }

        byte[] data;
        int startCodeLength = 0;

        // Check if already has start code
        if (naluData.Length >= 4 &&
            naluData[0] == 0 && naluData[1] == 0 && naluData[2] == 0 && naluData[3] == 1)
        {
            startCodeLength = 4;
            data = naluData.ToArray();
        }
        else if (naluData.Length >= 3 &&
                 naluData[0] == 0 && naluData[1] == 0 && naluData[2] == 1)
        {
            startCodeLength = 3;
            data = naluData.ToArray();
        }
        else if (ensureStartCode)
        {
            // Add Annex-B start code
            data = new byte[naluData.Length + 4];
            data[0] = 0;
            data[1] = 0;
            data[2] = 0;
            data[3] = 1;
            naluData.CopyTo(data.AsSpan(4));
            startCodeLength = 4;
        }
        else
        {
            data = naluData.ToArray();
        }

        var nalu = new H264Nalu(data, startCodeLength);

        // TryAdd is non-blocking and thread-safe
        bool added = _naluQueue.TryAdd(nalu, 0);

        if (!added && _logger != null && _logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning("RTP NALU queue full, frame dropped (size: {Size})", naluData.Length);
        }

        if (added && _logger != null && _logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Pushed RTP NALU to queue (size: {Size})", naluData.Length);
        }

        return added;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync();
    }
}
