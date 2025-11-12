using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace SharpVideo.RtpPlayerDemo.Rtp;

/// <summary>
/// A communications channel for transmitting and receiving Real-time Protocol (RTP) and
/// Real-time Control Protocol (RTCP) packets. This class performs the socket management
/// functions.
/// </summary>
internal class RTPChannel : IDisposable
{
    private readonly ILogger _logger;

    /// <summary>
    /// The local port we are listening for RTP (and whatever else is multiplexed) packets on.
    /// </summary>
    private readonly int _rtpPort;

    /// <summary>
    /// The local port we are listening for RTCP packets on.
    /// </summary>
    private readonly int _controlPort;

    private readonly Socket _rtpSocket;

    private UdpReceiver _rtpReceiver;
    private readonly Socket _controlSocket;
    private UdpReceiver _controlReceiver;
    private bool _rtpReceiverStarted = false;
    private bool _controlReceiverStarted = false;
    private bool _isClosed;

    /// <summary>
    /// Creates a new RTP channel. The RTP and optionally RTCP sockets will be bound in the constructor.
    /// They do not start receiving until the Start method is called.
    /// </summary>
    /// <param name="createControlSocket">Set to true if a separate RTCP control socket should be created. If RTP and
    /// RTCP are being multiplexed (as they are for WebRTC) there's no need to a separate control socket.</param>
    /// <param name="bindAddress">Optional. An IP address belonging to a local interface that will be used to bind
    /// the RTP and control sockets to. If left empty then the IPv6 any address will be used if IPv6 is supported
    /// and fallback to the IPv4 any address.</param>
    /// <param name="bindPort">Optional. The specific port to attempt to bind the RTP port on.</param>
    public RTPChannel(bool createControlSocket, IPAddress bindAddress, int bindPort, ILogger logger)
    {
        _logger = logger;
        NetServices.CreateRtpSocket(createControlSocket, bindAddress, bindPort, out var rtpSocket, out _controlSocket);

        if (rtpSocket == null)
        {
            throw new ApplicationException("The RTP channel was not able to create an RTP socket.");
        }
        else if (createControlSocket && _controlSocket == null)
        {
            throw new ApplicationException("The RTP channel was not able to create a Control socket.");
        }

        _rtpSocket = rtpSocket;
        var rtpLocalEndPoint = _rtpSocket.LocalEndPoint as IPEndPoint;
        _rtpPort = rtpLocalEndPoint.Port;
        var controlLocalEndPoint = (_controlSocket != null) ? _controlSocket.LocalEndPoint as IPEndPoint : null;
        _controlPort = (_controlSocket != null) ? controlLocalEndPoint.Port : 0;
    }


    public event Action<int, IPEndPoint, byte[]> OnRtpDataReceived;
    public event Action<string> OnClosed;

    /// <summary>
    /// Starts listening on the RTP and control ports.
    /// </summary>
    public void Start()
    {
        StartRtpReceiver();
        //StartControlReceiver();
    }

    /// <summary>
    /// Starts the UDP receiver that listens for RTP packets.
    /// </summary>
    private void StartRtpReceiver()
    {
        if (!_rtpReceiverStarted)
        {
            _rtpReceiverStarted = true;

            _logger.LogDebug($"RTPChannel for {_rtpSocket.LocalEndPoint} started.");

            _rtpReceiver = new UdpReceiver(_rtpSocket);
            _rtpReceiver.OnPacketReceived += OnRTPPacketReceived;
            _rtpReceiver.OnClosed += Close;
            _rtpReceiver.BeginReceiveFrom();
        }
    }

    /// <summary>
    /// Closes the session's RTP and control ports.
    /// </summary>
    public void Close(string reason)
    {
        if (!_isClosed)
        {
            try
            {
                string closeReason = reason ?? "normal";

                if (_controlReceiver == null)
                {
                    _logger.LogDebug($"RTPChannel closing, RTP receiver on port {_rtpPort}. Reason: {closeReason}.");
                }
                else
                {
                    _logger.LogDebug($"RTPChannel closing, RTP receiver on port {_rtpPort}, Control receiver on port {_controlPort}. Reason: {closeReason}.");
                }

                _isClosed = true;
                _rtpReceiver?.Close(null);
                _controlReceiver?.Close(null);

                OnClosed?.Invoke(closeReason);
            }
            catch (Exception excp)
            {
                _logger.LogError("Exception RTPChannel.Close. " + excp);
            }
        }
    }

    /// <summary>
    /// Event handler for packets received on the RTP UDP socket.
    /// </summary>
    /// <param name="receiver">The UDP receiver the packet was received on.</param>
    /// <param name="localPort">The local port it was received on.</param>
    /// <param name="remoteEndPoint">The remote end point of the sender.</param>
    /// <param name="packet">The raw packet received (note this may not be RTP if other protocols are being multiplexed).</param>
    protected virtual void OnRTPPacketReceived(UdpReceiver receiver, int localPort, IPEndPoint remoteEndPoint, byte[] packet)
    {
        if (packet?.Length > 0)
        {
            OnRtpDataReceived?.Invoke(localPort, remoteEndPoint, packet);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        Close(null);
    }

    public void Dispose()
    {
        Close(null);
    }
}
