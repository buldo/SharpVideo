using System.Net.Sockets;
using Rtsp.Rtp;

namespace SharpVideo.RtpPlayerDemo;

/// <summary>
/// Receives H264 video frames from UDP RTP packets.
/// </summary>
public sealed class H264UdpReceiver : IDisposable
{
    private readonly H264Payload _h264Payload = new();
    private readonly UdpClient _udpClient;
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveTask;
    private bool _disposed;

    /// <summary>
    /// Event raised when a complete H264 frame is received.
    /// </summary>
    public event EventHandler<RawMediaFrame>? FrameReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="H264UdpReceiver"/> class.
    /// </summary>
    /// <param name="port">The UDP port to listen on.</param>
    public H264UdpReceiver(int port)
    {
        _udpClient = new UdpClient(port);
    }

    /// <summary>
    /// Starts receiving UDP packets.
    /// </summary>
    public void Start()
    {
        _receiveTask = Task.Run(ReceiveLoopAsync);
    }

    /// <summary>
    /// Stops receiving UDP packets.
    /// </summary>
    public async Task StopAsync()
    {
        await _cts.CancelAsync();
        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
            }
        }
    }

    private async Task ReceiveLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(_cts.Token);
                ProcessPacket(result.Buffer);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                // Socket was closed
                break;
            }
        }
    }

    private void ProcessPacket(byte[] data)
    {
        var packet = new RtpPacket(data);
        var frame = _h264Payload.ProcessPacket(packet);
        if (frame.Any())
        {
            FrameReceived?.Invoke(this, frame);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        _udpClient.Dispose();
        _cts.Dispose();
    }
}