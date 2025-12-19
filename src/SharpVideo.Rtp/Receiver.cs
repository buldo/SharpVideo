using System.Net;
using Microsoft.Extensions.Logging;

namespace SharpVideo.RtpPlayerDemo.Rtp;

public class Receiver
{
    private static int _nextIndex;

    private readonly IPEndPoint _bindEndPoint;
    private readonly VideoStream _videoStream;
    private readonly RTPChannel _channel;

    public Receiver(IPEndPoint bindEndPoint, ILogger<Receiver> logger)
    {
        _bindEndPoint = bindEndPoint;
        var sessionConfig = new RtpSessionConfig
        {
            BindAddress = _bindEndPoint.Address,
            BindPort = _bindEndPoint.Port,
            IsMediaMultiplexed = false,
        };
        _videoStream = new VideoStream(sessionConfig, _nextIndex, logger);
        _videoStream.OnVideoFrameReceivedByIndex += VideoStreamOnOnVideoFrameReceivedByIndex;
        _channel = new RTPChannel(false, sessionConfig.BindAddress, sessionConfig.BindPort, logger);
        _videoStream.AddRtpChannel(_channel);
        _channel.OnRtpDataReceived += OnReceiveRTPPacket;

        _nextIndex++;
    }

    public event Action<int, IPEndPoint, uint, byte[]> OnVideoFrameReceivedByIndex;

    public void Start()
    {
        _channel.Start();
    }

    private void VideoStreamOnOnVideoFrameReceivedByIndex(int arg1, IPEndPoint arg2, uint arg3, byte[] arg4)
    {
        //Console.WriteLine($"{arg1}::{arg2}::{arg3}::{arg5}");
        OnVideoFrameReceivedByIndex?.Invoke(arg1, arg2, arg3, arg4);
    }

    private void OnReceiveRTPPacket(int localPort, IPEndPoint remoteEndPoint, byte[] buffer)
    {
        var hdr = new RTPHeader(buffer);
        _videoStream.OnReceiveRTPPacket(hdr, localPort, remoteEndPoint, buffer, _videoStream);
    }
}
