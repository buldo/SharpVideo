using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;

using Rtsp.Rtp;

namespace SharpVideo.RtpPlayerDemo;

/// <summary>
/// Service wrapper for RTP receiver that provides H.264 NAL units to decoder
/// </summary>
public class RtpReceiverService : IDisposable
{
    private readonly H264UdpReceiver _receiver;
    private readonly ILogger<RtpReceiverService> _logger;
    private readonly BlockingCollection<byte[]> _nalUnitsQueue = new(boundedCapacity: 100);
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public RtpReceiverService(IPEndPoint bindEndPoint, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<RtpReceiverService>();
        _receiver = new(bindEndPoint.Port);
        _receiver.FrameReceived += OnVideoFrameReceived;

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

    private void OnVideoFrameReceived(object? sender, RawMediaFrame frame)
    {
        ReceivedFramesCount++;

        foreach (ReadOnlyMemory<byte> memory in frame.Data)
        {
            _nalUnitsQueue.Add(memory.ToArray());
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
