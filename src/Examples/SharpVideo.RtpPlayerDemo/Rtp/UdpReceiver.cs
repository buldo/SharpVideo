using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SharpVideo.RtpPlayerDemo.Rtp;

internal delegate void PacketReceivedDelegate(UdpReceiver receiver, int localPort, IPEndPoint remoteEndPoint, byte[] packet);

/// <summary>
/// A basic UDP socket manager. The RTP channel may need both an RTP and Control socket. This class encapsulates
/// the common logic for UDP socket management.
/// </summary>
/// <remarks>
/// .NET Framework Socket source:
/// https://referencesource.microsoft.com/#system/net/system/net/Sockets/Socket.cs
/// .NET Core Socket source:
/// https://github.com/dotnet/runtime/blob/master/src/libraries/System.Net.Sockets/src/System/Net/Sockets/Socket.cs
/// Mono Socket source:
/// https://github.com/mono/mono/blob/master/mcs/class/System/System.Net.Sockets/Socket.cs
/// </remarks>
internal class UdpReceiver
{
    /// <summary>
    /// MTU is 1452 bytes so this should be heaps.
    /// TODO: What about fragmented UDP packets that are put back together by the OS?
    /// </summary>
    private const int RECEIVE_BUFFER_SIZE = 2048;

    private static ILogger logger = new NullLogger<UdpReceiver>();

    private readonly Socket _socket;
    private byte[] _recvBuffer;
    private bool _isClosed;
    private bool _isRunningReceive;
    private readonly IPEndPoint _localEndPoint;
    private readonly AddressFamily _addressFamily;

    public virtual bool IsClosed
    {
        get => _isClosed;
        protected set
        {
            if (_isClosed == value)
            {
                return;
            }
            _isClosed = value;
        }
    }

    public virtual bool IsRunningReceive
    {
        get => _isRunningReceive;
        protected set
        {
            if (_isRunningReceive == value)
            {
                return;
            }
            _isRunningReceive = value;
        }
    }

    /// <summary>
    /// Fires when a new packet has been received on the UDP socket.
    /// </summary>
    public event PacketReceivedDelegate OnPacketReceived;

    /// <summary>
    /// Fires when there is an error attempting to receive on the UDP socket.
    /// </summary>
    public event Action<string> OnClosed;

    public UdpReceiver(Socket socket, int mtu = RECEIVE_BUFFER_SIZE)
    {
        _socket = socket;
        _localEndPoint = _socket.LocalEndPoint as IPEndPoint;
        _recvBuffer = new byte[mtu];
        _addressFamily = _socket.LocalEndPoint.AddressFamily;
    }

    /// <summary>
    /// Starts the receive. This method returns immediately. An event will be fired in the corresponding "End" event to
    /// return any data received.
    /// </summary>
    public virtual void BeginReceiveFrom()
    {
        //Prevent call BeginReceiveFrom if it is already running
        if (_isClosed && _isRunningReceive)
        {
            _isRunningReceive = false;
        }
        if (_isRunningReceive || _isClosed)
        {
            return;
        }

        try
        {
            _isRunningReceive = true;
            EndPoint recvEndPoint = _addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
            _socket.BeginReceiveFrom(_recvBuffer, 0, _recvBuffer.Length, SocketFlags.None, ref recvEndPoint, EndReceiveFrom, null);
        }
        // Thrown when socket is closed. Can be safely ignored.
        // This exception can be thrown in response to an ICMP packet. The problem is the ICMP packet can be a false positive.
        // For example if the remote RTP socket has not yet been opened the remote host could generate an ICMP packet for the
        // initial RTP packets. Experience has shown that it's not safe to close an RTP connection based solely on ICMP packets.
        catch (ObjectDisposedException)
        {
            _isRunningReceive = false;
        }
        catch (SocketException socketException)
        {
            _isRunningReceive = false;
            logger.LogWarning($"Socket error {socketException.SocketErrorCode} in UdpReceiver.BeginReceiveFrom. {socketException.Message}");
            //Close(sockExcp.Message);
        }
        catch (Exception exception)
        {
            _isRunningReceive = false;
            // From https://github.com/dotnet/corefx/blob/e99ec129cfd594d53f4390bf97d1d736cff6f860/src/System.Net.Sockets/src/System/Net/Sockets/Socket.cs#L3262
            // the BeginReceiveFrom will only throw if there is an problem with the arguments or the socket has been disposed of. In that
            // case the socket can be considered to be unusable and there's no point trying another receive.
            logger.LogError(exception, $"Exception UdpReceiver.BeginReceiveFrom. {exception.Message}");
            Close(exception.Message);
        }
    }

    /// <summary>
    /// Handler for end of the begin receive call.
    /// </summary>
    /// <param name="ar">Contains the results of the receive.</param>
    private void EndReceiveFrom(IAsyncResult ar)
    {
        try
        {
            // When socket is closed the object will be disposed of in the middle of a receive.
            if (!_isClosed)
            {
                EndPoint remoteEP = _addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
                int bytesRead = _socket.EndReceiveFrom(ar, ref remoteEP);

                if (bytesRead > 0)
                {
                    // During experiments IPPacketInformation wasn't getting set on Linux. Without it the local IP address
                    // cannot be determined when a listener was bound to IPAddress.Any (or IPv6 equivalent). If the caller
                    // is relying on getting the local IP address on Linux then something may fail.
                    //if (packetInfo != null && packetInfo.Address != null)
                    //{
                    //    localEndPoint = new IPEndPoint(packetInfo.Address, localEndPoint.Port);
                    //}

                    byte[] packetBuffer = new byte[bytesRead];
                    // TODO: When .NET Framework support is dropped switch to using a slice instead of a copy.
                    Buffer.BlockCopy(_recvBuffer, 0, packetBuffer, 0, bytesRead);
                    CallOnPacketReceivedCallback(_localEndPoint.Port, remoteEP as IPEndPoint, packetBuffer);
                }
            }

            // If there is still data available it should be read now. This is more efficient than calling
            // BeginReceiveFrom which will incur the overhead of creating the callback and then immediately firing it.
            // It also avoids the situation where if the application cannot keep up with the network then BeginReceiveFrom
            // will be called synchronously (if data is available it calls the callback method immediately) which can
            // create a very nasty stack.
            if (!_isClosed && _socket.Available > 0)
            {
                while (!_isClosed && _socket.Available > 0)
                {
                    EndPoint remoteEP = _addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
                    int bytesReadSync = _socket.ReceiveFrom(_recvBuffer, 0, _recvBuffer.Length, SocketFlags.None, ref remoteEP);

                    if (bytesReadSync > 0)
                    {
                        byte[] packetBufferSync = new byte[bytesReadSync];
                        // TODO: When .NET Framework support is dropped switch to using a slice instead of a copy.
                        Buffer.BlockCopy(_recvBuffer, 0, packetBufferSync, 0, bytesReadSync);
                        CallOnPacketReceivedCallback(_localEndPoint.Port, remoteEP as IPEndPoint, packetBufferSync);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        catch (SocketException resetSockExcp) when (resetSockExcp.SocketErrorCode == SocketError.ConnectionReset)
        {
            // Thrown when close is called on a socket from this end. Safe to ignore.
        }
        catch (SocketException socketException)
        {
            // Socket errors do not trigger a close. The reason being that there are genuine situations that can cause them during
            // normal RTP operation. For example:
            // - the RTP connection may start sending before the remote socket starts listening,
            // - an on hold, transfer, etc. operation can change the RTP end point which could result in socket errors from the old
            //   or new socket during the transition.
            // It also seems that once a UDP socket pair have exchanged packets and the remote party closes the socket exception will occur
            // in the BeginReceive method (very handy). Follow-up, this doesn't seem to be the case, the socket exception can occur in
            // BeginReceive before any packets have been exchanged. This means it's not safe to close if BeginReceive gets an ICMP
            // error since the remote party may not have initialised their socket yet.
            logger.LogWarning(socketException, $"SocketException UdpReceiver.EndReceiveFrom ({socketException.SocketErrorCode}). {socketException.Message}");
        }
        catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
        { }
        catch (Exception excp)
        {
            logger.LogError($"Exception UdpReceiver.EndReceiveFrom. {excp}");
            Close(excp.Message);
        }
        finally
        {
            _isRunningReceive = false;
            if (!_isClosed)
            {
                BeginReceiveFrom();
            }
        }
    }

    /// <summary>
    /// Closes the socket and stops any new receives from being initiated.
    /// </summary>
    public virtual void Close(string reason)
    {
        if (!_isClosed)
        {
            _isClosed = true;
            _socket?.Close();

            OnClosed?.Invoke(reason);
        }
    }

    protected virtual void CallOnPacketReceivedCallback(int localPort, IPEndPoint remoteEndPoint, byte[] packet)
    {
        OnPacketReceived?.Invoke(this, localPort, remoteEndPoint, packet);
    }
}
