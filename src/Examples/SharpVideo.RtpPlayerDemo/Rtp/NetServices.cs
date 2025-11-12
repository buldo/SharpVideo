using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SharpVideo.RtpPlayerDemo.Rtp;

/// <summary>
/// Helper class to provide network services.
/// </summary>
internal class NetServices
{
    private const int RTP_RECEIVE_BUFFER_SIZE = 1000000;
    private const int RTP_SEND_BUFFER_SIZE = 1000000;

    /// <summary>
    /// The maximum number of re-attempts that will be made when trying to bind a UDP socket.
    /// </summary>
    private const int MAXIMUM_UDP_PORT_BIND_ATTEMPTS = 25;

    private static ILogger logger = new NullLogger<NetServices>();

    /// <summary>
    /// Doing the same check as here https://github.com/dotnet/corefx/blob/e99ec129cfd594d53f4390bf97d1d736cff6f860/src/System.Net.Sockets/src/System/Net/Sockets/SocketPal.Unix.cs#L19.
    /// Which is checking if a dual mode socket can use the *ReceiveFrom* methods in order to
    /// be able to get the remote destination end point.
    /// To date the only case this has cropped up for is Mac OS as per https://github.com/sipsorcery/sipsorcery/issues/207.
    /// </summary>
    private static bool? _supportsDualModeIPv4PacketInfo = null;
    public static bool SupportsDualModeIPv4PacketInfo
    {
        get
        {
            if (!_supportsDualModeIPv4PacketInfo.HasValue)
            {
                try
                {
                    _supportsDualModeIPv4PacketInfo = DoCheckSupportsDualModeIPv4PacketInfo();
                }
                catch
                {
                    _supportsDualModeIPv4PacketInfo = false;
                }
            }

            return _supportsDualModeIPv4PacketInfo.Value;
        }
    }

    /// <summary>
    /// Checks whether an IP address can be used on the underlying System.
    /// </summary>
    /// <param name="bindAddress">The bind address to use.</param>
    private static void CheckBindAddressAndThrow(IPAddress bindAddress)
    {
        if (bindAddress != null && bindAddress.AddressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6)
        {
            throw new ApplicationException("A UDP socket cannot be created on an IPv6 address due to lack of OS support.");
        }
        else if (bindAddress != null && bindAddress.AddressFamily == AddressFamily.InterNetwork && !Socket.OSSupportsIPv4)
        {
            throw new ApplicationException("A UDP socket cannot be created on an IPv4 address due to lack of OS support.");
        }
    }

    /// <summary>
    /// Attempts to create and bind a socket with defined protocol. The socket is always created with the ExclusiveAddressUse socket option
    /// set to accommodate a Windows 10 .Net Core socket bug where the same port can be bound to two different
    /// sockets, see https://github.com/dotnet/runtime/issues/36618.
    /// </summary>
    /// <param name="port">The port to attempt to bind on. Set to 0 to request the underlying OS to select a port.</param>
    /// <param name="bindAddress">Optional. If specified the socket will attempt to bind using this specific address.
    /// If not specified the broadest possible address will be chosen. Either IPAddress.Any or IPAddress.IPv6Any.</param>
    /// <param name="protocolType">Optional. If specified the socket procotol</param>
    /// <param name="requireEvenPort">If true the method will only return successfully if it is able to bind on an
    /// even numbered port.</param>
    /// <param name="useDualMode">If true then IPv6 sockets will be created as dual mode IPv4/IPv6 on supporting systems.</param>
    /// <returns>A bound socket if successful or throws an ApplicationException if unable to bind.</returns>
    private static Socket CreateBoundSocket(int port, IPAddress bindAddress, ProtocolType protocolType, bool requireEvenPort = false, bool useDualMode = true)
    {
        if (requireEvenPort && port != 0 && port % 2 != 0)
        {
            throw new ArgumentException("Cannot specify both require even port and a specific non-even port to bind on. Set port to 0.");
        }

        if (bindAddress == null)
        {
            bindAddress = (Socket.OSSupportsIPv6 && SupportsDualModeIPv4PacketInfo) ? IPAddress.IPv6Any : IPAddress.Any;
        }

        IPEndPoint logEp = new IPEndPoint(bindAddress, port);
        logger.LogDebug($"CreateBoundSocket attempting to create and bind socket(s) on {logEp} using protocol {protocolType}.");

        CheckBindAddressAndThrow(bindAddress);

        int bindAttempts = 0;
        AddressFamily addressFamily = bindAddress.AddressFamily;
        bool success = false;
        Socket socket = null;

        while (bindAttempts < MAXIMUM_UDP_PORT_BIND_ATTEMPTS)
        {
            try
            {
                socket = CreateSocket(addressFamily, protocolType, useDualMode);
                BindSocket(socket, bindAddress, port);
                int boundPort = (socket.LocalEndPoint as IPEndPoint).Port;

                if (requireEvenPort && boundPort % 2 != 0 && boundPort == IPEndPoint.MaxPort)
                {
                    logger.LogDebug($"CreateBoundSocket even port required, closing socket on {socket.LocalEndPoint}, max port reached request new bind.");
                    success = false;
                }
                else
                {
                    if (requireEvenPort && boundPort % 2 != 0)
                    {
                        logger.LogDebug($"CreateBoundSocket even port required, closing socket on {socket.LocalEndPoint} and retrying on {boundPort + 1}.");

                        // Close the socket, create a new one and try binding on the next consecutive port.
                        socket.Close();
                        socket = CreateSocket(addressFamily, protocolType, useDualMode);
                        BindSocket(socket, bindAddress, boundPort + 1);
                    }
                    else
                    {
                        if (addressFamily == AddressFamily.InterNetworkV6)
                        {
                            logger.LogDebug($"CreateBoundSocket successfully bound on {socket.LocalEndPoint}, dual mode {socket.DualMode}.");
                        }
                        else
                        {
                            logger.LogDebug($"CreateBoundSocket successfully bound on {socket.LocalEndPoint}.");
                        }
                    }

                    success = true;
                }
            }
            catch (SocketException sockExcp)
            {
                if (sockExcp.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    // Try again if the port is already in use.
                    logger.LogWarning($"Address already in use exception attempting to bind socket, attempt {bindAttempts}.");
                    success = false;
                }
                else if (sockExcp.SocketErrorCode == SocketError.AccessDenied)
                {
                    // This exception seems to be interchangeable with address already in use. Perhaps a race condition with another process
                    // attempting to bind at the same time.
                    logger.LogWarning($"Access denied exception attempting to bind socket, attempt {bindAttempts}.");
                    success = false;
                }
                else
                {
                    logger.LogError($"SocketException in NetServices.CreateBoundSocket. {sockExcp}");
                    throw;
                }
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception in NetServices.CreateBoundSocket attempting the initial socket bind on address {bindAddress}. {excp}");
                throw;
            }
            finally
            {
                if (!success)
                {
                    socket?.Close();
                }
            }

            if (success || port != 0)
            {
                // If the bind was requested on a specific port there is no need to try again.
                break;
            }
            else
            {
                bindAttempts++;
            }
        }

        if (success)
        {
            return socket;
        }
        else
        {
            throw new ApplicationException($"Unable to bind socket using end point {logEp}.");
        }
    }

    private static void BindSocket(Socket socket, IPAddress bindAddress, int port)
    {
        // Nasty code warning. On Windows Subsystem for Linux (WSL) on Windows 10
        // the OS lets a socket bind on an IPv6 dual mode port even if there
        // is an IPv4 socket bound to the same port. To prevent this occurring
        // a test IPv4 socket bind is carried out.
        // This happen even if the exclusive address socket option is set.
        // See https://github.com/dotnet/runtime/issues/36618.
        if (port != 0 &&
            socket.AddressFamily == AddressFamily.InterNetworkV6 &&
            socket.DualMode && IPAddress.IPv6Any.Equals(bindAddress) &&
            Environment.OSVersion.Platform == PlatformID.Unix &&
            RuntimeInformation.OSDescription.Contains("Microsoft"))
        {
            // Create a dummy IPv4 socket and attempt to bind it to the same port
            // to check the port isn't already in use.
            if (Socket.OSSupportsIPv4)
            {
                logger.LogDebug($"WSL detected, carrying out bind check on 0.0.0.0:{port}.");

                using (Socket testSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    testSocket.Bind(new IPEndPoint(IPAddress.Any, port));
                    testSocket.Close();
                }
            }
        }

        socket.Bind(new IPEndPoint(bindAddress, port));
    }

    private static Socket CreateSocket(AddressFamily addressFamily, ProtocolType protocol, bool useDualMode = true)
    {
        var sock = new Socket(addressFamily, protocol == ProtocolType.Tcp ? SocketType.Stream : SocketType.Dgram, protocol);
        sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);

        if (addressFamily == AddressFamily.InterNetworkV6)
        {
            if (!useDualMode)
            {
                sock.DualMode = false;
            }
            else
            {
                sock.DualMode = SupportsDualModeIPv4PacketInfo;
            }
        }
        return sock;
    }

    /// <summary>
    /// Attempts to create and bind a new RTP UDP Socket, and optionally an control (RTCP), socket(s).
    /// The RTP and control sockets created are IPv4 and IPv6 dual mode sockets which means they can send and receive
    /// either IPv4 or IPv6 packets.
    /// </summary>
    /// <param name="createControlSocket">True if a control (RTCP) socket should be created. Set to false if RTP
    /// and RTCP are being multiplexed on the same connection.</param>
    /// <param name="bindAddress">Optional. If null The RTP and control sockets will be created as IPv4 and IPv6 dual mode
    /// sockets which means they can send and receive either IPv4 or IPv6 packets. If the bind address is specified an attempt
    /// will be made to bind the RTP and optionally control listeners on it.</param>
    /// <param name="bindPort">Optional. If 0 the choice of port will be left up to the Operating System. If specified
    /// a single attempt will be made to bind on the port.</param>
    /// <param name="portRange">Optional. If non-null the choice of port will be left up to the PortRange. Multiple ports will be
    /// tried before giving up. The parameter bindPort is ignored.</param>
    /// <param name="rtpSocket">An output parameter that will contain the allocated RTP socket.</param>
    /// <param name="controlSocket">An output parameter that will contain the allocated control (RTCP) socket.</param>
    public static void CreateRtpSocket(bool createControlSocket, IPAddress bindAddress, int bindPort, out Socket? rtpSocket, out Socket controlSocket)
    {
        CreateRtpSocket(createControlSocket, ProtocolType.Udp, bindAddress, bindPort, out rtpSocket, out controlSocket);
    }

    /// <summary>
    /// Attempts to create and bind a new RTP Socket with protocol, and optionally an control (RTCP), socket(s).
    /// The RTP and control sockets created are IPv4 and IPv6 dual mode sockets which means they can send and receive
    /// either IPv4 or IPv6 packets.
    /// </summary>
    /// <param name="createControlSocket">True if a control (RTCP) socket should be created. Set to false if RTP
    /// and RTCP are being multiplexed on the same connection.</param>
    /// <param name="protocolType">Procotol used by socket</param>
    /// <param name="bindAddress">Optional. If null The RTP and control sockets will be created as IPv4 and IPv6 dual mode
    /// sockets which means they can send and receive either IPv4 or IPv6 packets. If the bind address is specified an attempt
    /// will be made to bind the RTP and optionally control listeners on it.</param>
    /// <param name="bindPort">Optional. If 0 the choice of port will be left up to the Operating System. If specified
    /// a single attempt will be made to bind on the port.</param>
    /// <param name="portRange">Optional. If non-null the choice of port will be left up to the PortRange. Multiple ports will be
    /// tried before giving up. The parameter bindPort is ignored.</param>
    /// <param name="rtpSocket">An output parameter that will contain the allocated RTP socket.</param>
    /// <param name="controlSocket">An output parameter that will contain the allocated control (RTCP) socket.</param>
    private static void CreateRtpSocket(bool createControlSocket, ProtocolType protocolType, IPAddress bindAddress, int bindPort, out Socket? rtpSocket, out Socket controlSocket)
    {
        CheckBindAddressAndThrow(bindAddress);

        IPEndPoint bindEP = new IPEndPoint(bindAddress, bindPort);
        logger.LogDebug($"CreateRtpSocket attempting to create and bind RTP socket(s) on {bindEP}.");

        rtpSocket = null;
        controlSocket = null;
        int bindAttempts = 0;

        while (bindAttempts < MAXIMUM_UDP_PORT_BIND_ATTEMPTS)
        {
            try
            {
                rtpSocket = CreateBoundSocket(bindPort, bindAddress, protocolType, createControlSocket);
                rtpSocket.ReceiveBufferSize = RTP_RECEIVE_BUFFER_SIZE;
                rtpSocket.SendBufferSize = RTP_SEND_BUFFER_SIZE;

                if (createControlSocket)
                {
                    // For legacy VoIP the RTP and Control sockets need to be consecutive with the RTP port being
                    // an even number.
                    int rtpPort = (rtpSocket.LocalEndPoint as IPEndPoint).Port;
                    int controlPort = rtpPort + 1;

                    // Hopefully the next OS port allocation will be back in range.
                    if (controlPort <= IPEndPoint.MaxPort)
                    {
                        // This bind is being attempted on a specific port and can therefore legitimately fail if the port is already in use.
                        // Certain expected failure are caught and the attempt to bind two consecutive port will be re-attempted.
                        controlSocket = CreateBoundSocket(controlPort, bindAddress, protocolType);
                        controlSocket.ReceiveBufferSize = RTP_RECEIVE_BUFFER_SIZE;
                        controlSocket.SendBufferSize = RTP_SEND_BUFFER_SIZE;
                    }
                }
            }
            catch (ApplicationException) { }

            if ((rtpSocket != null && (!createControlSocket || controlSocket != null)) || (bindPort != 0))
            {
                // If a specific bind port was specified only a single attempt to create the socket is made.
                break;
            }
            else
            {
                rtpSocket?.Close();
                controlSocket?.Close();
                bindAttempts++;

                rtpSocket = null;
                controlSocket = null;

                logger.LogWarning($"CreateRtpSocket failed to create and bind RTP socket(s) on {bindEP}, bind attempt {bindAttempts}.");
            }
        }

        if (createControlSocket && rtpSocket != null && controlSocket != null)
        {
            if (rtpSocket.LocalEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
            {
                logger.LogDebug($"Successfully bound RTP socket {rtpSocket.LocalEndPoint} (dual mode {rtpSocket.DualMode}) and control socket {controlSocket.LocalEndPoint} (dual mode {controlSocket.DualMode}).");
            }
            else
            {
                logger.LogDebug($"Successfully bound RTP socket {rtpSocket.LocalEndPoint} and control socket {controlSocket.LocalEndPoint}.");
            }
        }
        else if (!createControlSocket && rtpSocket != null)
        {
            if (rtpSocket.LocalEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
            {
                logger.LogDebug($"Successfully bound RTP socket {rtpSocket.LocalEndPoint} (dual mode {rtpSocket.DualMode}).");
            }
            else
            {
                logger.LogDebug($"Successfully bound RTP socket {rtpSocket.LocalEndPoint}.");
            }
        }
        else
        {
            throw new ApplicationException($"Failed to create and bind RTP socket using bind address {bindAddress}.");
        }
    }

    /// <summary>
    /// Dual mode sockets are created by default if an IPv6 bind address was specified.
    /// Dual mode needs to be disabled for Mac OS sockets as they don't support the use
    /// of dual mode and the receive methods that return packet information. Packet info
    /// is needed to get the remote recipient.
    /// </summary>
    /// <returns>True if the underlying OS supports dual mode IPv6 sockets WITH the socket ReceiveFrom methods
    /// which are required to get the remote end point. False if not</returns>
    private static bool DoCheckSupportsDualModeIPv4PacketInfo()
    {
        bool hasDualModeReceiveSupport = true;

        if (!Socket.OSSupportsIPv6)
        {
            hasDualModeReceiveSupport = false;
        }
        else
        {
            var testSocket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            testSocket.DualMode = true;

            try
            {
                testSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
                testSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, 1);
                byte[] buf = new byte[1];
                EndPoint remoteEP = new IPEndPoint(IPAddress.IPv6Any, 0);

                testSocket.BeginReceiveFrom(buf, 0, buf.Length, SocketFlags.None, ref remoteEP, (ar) => { try { testSocket.EndReceiveFrom(ar, ref remoteEP); } catch { } }, null);
                hasDualModeReceiveSupport = true;
            }
            catch (PlatformNotSupportedException platExcp)
            {
                logger.LogWarning(platExcp, $"A socket 'receive from' attempt on a dual mode socket failed (dual mode RTP sockets will not be used) with a platform exception {platExcp.Message}");
                hasDualModeReceiveSupport = false;
            }
            catch (Exception excp)
            {
                logger.LogWarning(excp, $"A socket 'receive from' attempt on a dual mode socket failed (dual mode RTP sockets will not be used) with {excp.Message}");
                hasDualModeReceiveSupport = false;
            }
            finally
            {
                testSocket.Close();
            }
        }

        return hasDualModeReceiveSupport;
    }
}
