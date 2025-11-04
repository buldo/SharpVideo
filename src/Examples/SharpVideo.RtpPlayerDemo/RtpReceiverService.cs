using System.Collections.Concurrent;
using System.Net;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.RtpPlayerDemo.Rtp;

namespace SharpVideo.RtpPlayerDemo;

/// <summary>
/// Service wrapper for RTP receiver that provides H.264 NAL units to decoder
/// </summary>
[SupportedOSPlatform("linux")]
public class RtpReceiverService : IDisposable
{
    private readonly Receiver _receiver;
    private readonly ILogger<RtpReceiverService> _logger;
    private readonly BlockingCollection<byte[]> _nalUnitsQueue = new(boundedCapacity: 30);
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public RtpReceiverService(IPEndPoint bindEndPoint, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<RtpReceiverService>();
        var receiverLogger = loggerFactory.CreateLogger<Receiver>();
        _receiver = new Receiver(bindEndPoint, receiverLogger);
        _receiver.OnVideoFrameReceivedByIndex += OnVideoFrameReceived;
        
        _logger.LogInformation("RTP receiver initialized on {EndPoint}", bindEndPoint);
    }

    /// <summary>
    /// Total number of received frames
    /// </summary>
    public int ReceivedFramesCount { get; private set; }

    /// <summary>
    /// Number of frames dropped due to queue overflow
    /// </summary>
    public int DroppedFramesCount { get; private set; }

    /// <summary>
    /// Start receiving RTP packets
    /// </summary>
    public void Start()
    {
        _receiver.Start();
        _logger.LogInformation("RTP receiver started");
    }

    /// <summary>
    /// Try to get next NAL unit from queue
    /// </summary>
    public bool TryGetNalUnit(out byte[] nalUnit, CancellationToken cancellationToken)
    {
        nalUnit = null!;
        try
        {
            return _nalUnitsQueue.TryTake(out nalUnit!, 100, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void OnVideoFrameReceived(int streamIndex, IPEndPoint remoteEndPoint, uint timestamp, byte[] nalUnit)
    {
        ReceivedFramesCount++;

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Received NAL unit: stream={Stream}, remote={Remote}, timestamp={Timestamp}, size={Size}",
                streamIndex, remoteEndPoint, timestamp, nalUnit.Length);
        }

        // Try to add to queue without blocking
        if (!_nalUnitsQueue.TryAdd(nalUnit, 0))
        {
            DroppedFramesCount++;
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("NAL unit queue full, dropping frame (total dropped: {Count})", DroppedFramesCount);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cts.Cancel();
        _nalUnitsQueue.CompleteAdding();
        _nalUnitsQueue.Dispose();
        _cts.Dispose();
        
        _logger.LogInformation("RTP receiver service disposed");
    }
}
