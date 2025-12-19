using System.Net;
using Microsoft.Extensions.Logging;

namespace SharpVideo.RtpPlayerDemo.Rtp;

internal class VideoStream
{
    private readonly ILogger _logger;

    /// <summary>
    /// Indicates the maximum frame size that can be reconstructed from RTP packets during the depacketisation
    /// process.
    /// </summary>
    private readonly int _maxReconstructedVideoFrameSize = 1048576;
    private RtpVideoFramer? _rtpVideoFramer;

    public VideoStream(
        RtpSessionConfig config,
        int index,
        ILogger logger)
    {
        RtpSessionConfig = config;
        this.Index = index;
        _logger = logger;
    }

    /// <summary>
    /// Gets fired when a full video frame is reconstructed from one or more RTP packets
    /// received from the remote party.
    /// </summary>
    /// <remarks>
    ///  - Received from end point,
    ///  - The frame timestamp,
    ///  - The encoded video frame payload.
    ///  - The video format of the encoded frame.
    /// </remarks>
    public event Action<int, IPEndPoint, uint, byte[]>? OnVideoFrameReceivedByIndex;

    private void ProcessVideoRtpFrame(IPEndPoint endpoint, RTPPacket packet, VideoCodecsEnum codec)
    {
        if (OnVideoFrameReceivedByIndex == null)
        {
            return;
        }

        if (_rtpVideoFramer != null)
        {
            var frame = _rtpVideoFramer.GotRtpPacket(packet);
            if (frame != null)
            {
                OnVideoFrameReceivedByIndex?.Invoke(Index, endpoint, packet.Header.Timestamp, frame);
            }
        }
        else
        {
            if (codec == VideoCodecsEnum.VP8 ||
                codec == VideoCodecsEnum.H264)
            {
                _logger.LogDebug("Video depacketisation codec set to {Codec} for SSRC {SyncSource}.", codec, packet.Header.SyncSource);

                _rtpVideoFramer = new RtpVideoFramer(codec, _maxReconstructedVideoFrameSize);

                var frame = _rtpVideoFramer.GotRtpPacket(packet);
                if (frame != null)
                {
                    OnVideoFrameReceivedByIndex?.Invoke(Index, endpoint, packet.Header.Timestamp, frame);
                }
            }
            else
            {
                _logger.LogWarning("Video depacketisation logic for codec {codec} has not been implemented, PR's welcome!", codec);
            }
        }
    }

    protected class PendingPackages
    {
        public RTPHeader hdr;
        public int localPort;
        public IPEndPoint remoteEndPoint;
        public byte[] buffer;
        public VideoStream videoStream;

        public PendingPackages(RTPHeader hdr, int localPort, IPEndPoint remoteEndPoint, byte[] buffer, VideoStream videoStream)
        {
            this.hdr = hdr;
            this.localPort = localPort;
            this.remoteEndPoint = remoteEndPoint;
            this.buffer = buffer;
            this.videoStream = videoStream;
        }
    }

    protected object _pendingPackagesLock = new object();
    protected List<PendingPackages> _pendingPackagesBuffer = new List<PendingPackages>();

    private RtpSessionConfig RtpSessionConfig;

    protected RTPChannel rtpChannel = null;

    protected bool _isClosed = false;

    public int Index = -1;

    #region EVENTS

    /// <summary>
    /// Fires when the connection for a media type is classified as timed out due to not
    /// receiving any RTP or RTCP packets within the given period.
    /// </summary>
    public event Action<int> OnTimeoutByIndex;

    /// <summary>
    /// Gets fired when an RTCP report is sent. This event is for diagnostics only.
    /// </summary>
    public event Action<int> OnSendReportByIndex;

    /// <summary>
    /// Gets fired when an RTP packet is received from a remote party.
    /// Parameters are:
    ///  - Remote endpoint packet was received from,
    ///  - The media type the packet contains, will be audio or video,
    ///  - The full RTP packet.
    /// </summary>
    public event Action<int, IPEndPoint, RTPPacket> OnRtpPacketReceivedByIndex;

    /// <summary>
    /// Gets fired when an RTP event is detected on the remote call party's RTP stream.
    /// </summary>
    public event Action<int, IPEndPoint, RTPEvent, RTPHeader> OnRtpEventByIndex;

    /// <summary>
    /// Gets fired when an RTCP report is received. This event is for diagnostics only.
    /// </summary>
    public event Action<int, IPEndPoint> OnReceiveReportByIndex;

    public event Action<bool> OnIsClosedStateChanged;

    #endregion EVENTS

    #region PROPERTIES

    public bool AcceptRtpFromAny => true;

    /// <summary>
    /// Indicates whether the session has been closed. Once a session is closed it cannot
    /// be restarted.
    /// </summary>
    public bool IsClosed
    {
        get
        {
            return _isClosed;
        }
        set
        {
            if (_isClosed == value)
            {
                return;
            }
            _isClosed = value;

            //Clear previous buffer
            ClearPendingPackages();

            OnIsClosedStateChanged?.Invoke(_isClosed);
        }
    }

    /// <summary>
    /// The remote video track. Will be null if the remote party is not sending this media
    /// </summary>
    public MediaStreamTrack? RemoteTrack { get; set; }

    /// <summary>
    /// The remote RTP end point this stream is sending media to.
    /// </summary>
    public IPEndPoint DestinationEndPoint { get; set; }


    #endregion PROPERTIES

    public bool EnsureBufferUnprotected(byte[] buf, RTPHeader header, out RTPPacket packet)
    {
        packet = new RTPPacket(buf);
        packet.Header.ReceivedTime = header.ReceivedTime;
        return true;
    }

    public void AddRtpChannel(RTPChannel rtpChannel)
    {
        this.rtpChannel = rtpChannel;
    }

    public void OnReceiveRTPPacket(RTPHeader hdr, int localPort, IPEndPoint remoteEndPoint, byte[] buffer, VideoStream videoStream = null)
    {
        RTPPacket? rtpPacket = null;
        //if (RemoteRtpEventPayloadID != 0 && hdr.PayloadType == RemoteRtpEventPayloadID)
        //{
        //    if (!EnsureBufferUnprotected(buffer, hdr, out rtpPacket))
        //    {
        //        // Cache pending packages to use it later to prevent missing frames
        //        // when DTLS was not completed yet as a Server bt already completed as a client
        //        AddPendingPackage(hdr, localPort, remoteEndPoint, buffer, videoStream);
        //        return;
        //    }

        //    RaiseOnRtpEventByIndex(remoteEndPoint, new RTPEvent(rtpPacket.Payload), rtpPacket.Header);
        //    return;
        //}

        // Set the remote track SSRC so that RTCP reports can match the media type.
        if (RemoteTrack != null && RemoteTrack.Ssrc == 0 && DestinationEndPoint != null)
        {
            bool isValidSource = AdjustRemoteEndPoint(hdr.SyncSource, remoteEndPoint);

            if (isValidSource)
            {
                _logger.LogDebug($"Set remote track (index={Index}) SSRC to {hdr.SyncSource}.");
                RemoteTrack.Ssrc = hdr.SyncSource;
            }
        }


        // Note AC 24 Dec 2020: The problem with waiting until the remote description is set is that the remote peer often starts sending
        // RTP packets at the same time it signals its SDP offer or answer. Generally this is not a problem for audio but for video streams
        // the first RTP packet(s) are the key frame and if they are ignored the video stream will take additional time or manual
        // intervention to synchronise.
        //if (RemoteDescription != null)
        //{

        // Don't hand RTP packets to the application until the remote description has been set. Without it
        // things like the common codec, DTMF support etc. are not known.

        //SDPMediaTypesEnum mediaType = (rtpMediaType.HasValue) ? rtpMediaType.Value : DEFAULT_MEDIA_TYPE;

        // For video RTP packets an attempt will be made to collate into frames. It's up to the application
        // whether it wants to subscribe to frames of RTP packets.

        rtpPacket = null;
        if (RemoteTrack != null)
        {
            LogIfWrongSeqNumber($"", hdr, RemoteTrack);
            ProcessHeaderExtensions(hdr);
        }
        if (!EnsureBufferUnprotected(buffer, hdr, out rtpPacket))
        {
            return;
        }

        // When receiving an Payload from other peer, it will be related to our LocalDescription,
        // not to RemoteDescription (as proved by Azure WebRTC Implementation)
        // TODO: Buldo
        var codec = GetFormatForPayloadID(hdr.PayloadType);
        if (rtpPacket != null && codec != null)
        {
            videoStream?.ProcessVideoRtpFrame(remoteEndPoint, rtpPacket, codec.Value);
            RaiseOnRtpPacketReceivedByIndex(remoteEndPoint, rtpPacket);
        }
    }
    private VideoCodecsEnum? GetFormatForPayloadID(int hdrPayloadType)
    {
        if (hdrPayloadType == 97 || hdrPayloadType == 96)
        {
            return VideoCodecsEnum.H264;
        }

        return null;
    }

    private void RaiseOnRtpPacketReceivedByIndex(IPEndPoint ipEndPoint, RTPPacket rtpPacket)
    {
        OnRtpPacketReceivedByIndex?.Invoke(Index, ipEndPoint, rtpPacket);
    }


    // Submit all previous cached packages to self
    protected virtual void DispatchPendingPackages()
    {
        PendingPackages[] pendingPackagesArray = null;

        var isContextValid = !IsClosed;

        lock (_pendingPackagesLock)
        {
            if (isContextValid)
            {
                pendingPackagesArray = _pendingPackagesBuffer.ToArray();
            }
            _pendingPackagesBuffer.Clear();
        }
        if (isContextValid)
        {
            foreach (var pendingPackage in pendingPackagesArray)
            {
                if (pendingPackage != null)
                {
                    OnReceiveRTPPacket(pendingPackage.hdr, pendingPackage.localPort, pendingPackage.remoteEndPoint, pendingPackage.buffer, pendingPackage.videoStream);
                }
            }
        }
    }

    // Clear previous buffer
    protected virtual void ClearPendingPackages()
    {
        lock (_pendingPackagesLock)
        {
            _pendingPackagesBuffer.Clear();
        }
    }

    private void LogIfWrongSeqNumber(string trackType, RTPHeader header, MediaStreamTrack track)
    {
        if (track.LastRemoteSeqNum != 0 &&
            header.SequenceNumber != (track.LastRemoteSeqNum + 1) &&
            !(header.SequenceNumber == 0 && track.LastRemoteSeqNum == ushort.MaxValue))
        {
            _logger.LogWarning($"{trackType} stream sequence number jumped from {track.LastRemoteSeqNum} to {header.SequenceNumber}.");
        }
    }

    /// <summary>
    /// Adjusts the expected remote end point for a particular media type.
    /// </summary>
    /// <param name="mediaType">The media type of the RTP packet received.</param>
    /// <param name="ssrc">The SSRC from the RTP packet header.</param>
    /// <param name="receivedOnEndPoint">The actual remote end point that the RTP packet came from.</param>
    /// <returns>True if remote end point for this media type was the expected one or it was adjusted. False if
    /// the remote end point was deemed to be invalid for this media type.</returns>
    private bool AdjustRemoteEndPoint(uint ssrc, IPEndPoint receivedOnEndPoint)
    {
        bool isValidSource = false;
        IPEndPoint expectedEndPoint = DestinationEndPoint;

        if (expectedEndPoint.Address.Equals(receivedOnEndPoint.Address) && expectedEndPoint.Port == receivedOnEndPoint.Port)
        {
            // Exact match on actual and expected destination.
            isValidSource = true;
        }
        else if (AcceptRtpFromAny || (expectedEndPoint.Address.IsPrivate() && !receivedOnEndPoint.Address.IsPrivate())
                //|| (IPAddress.Loopback.Equals(receivedOnEndPoint.Address) || IPAddress.IPv6Loopback.Equals(receivedOnEndPoint.Address
                )
        {
            // The end point doesn't match BUT we were supplied a private address in the SDP and the remote source is a public address
            // so high probability there's a NAT on the network path. Switch to the remote end point (note this can only happen once
            // and only if the SSRV is 0, i.e. this is the first RTP packet.
            // If the remote end point is a loopback address then it's likely that this is a test/development
            // scenario and the source can be trusted.
            // AC 12 Jul 2020: Commented out the expression that allows the end point to be change just because it's a loopback address.
            // A breaking case is doing an attended transfer test where two different agents are using loopback addresses.
            // The expression allows an older session to override the destination set by a newer remote SDP.
            // AC 18 Aug 2020: Despite the carefully crafted rules below and https://github.com/sipsorcery/sipsorcery/issues/197
            // there are still cases that were a problem in one scenario but acceptable in another. To accommodate a new property
            // was added to allow the application to decide whether the RTP end point switches should be liberal or not.
            _logger.LogDebug($" end point switched for RTP ssrc {ssrc} from {expectedEndPoint} to {receivedOnEndPoint}.");

            DestinationEndPoint = receivedOnEndPoint;

            isValidSource = true;
        }
        else
        {
            _logger.LogWarning($"RTP packet with SSRC {ssrc} received from unrecognised end point {receivedOnEndPoint}.");
        }

        return isValidSource;
    }

    private void ProcessHeaderExtensions(RTPHeader header)
    {
        header.GetHeaderExtensions().ToList().ForEach(x =>
        {
            if (RemoteTrack != null)
            {
                var ntpTimestamp = x.GetNtpTimestamp(RemoteTrack.HeaderExtensions);
                if (ntpTimestamp.HasValue)
                {
                    new TimestampPair() { NtpTimestamp = ntpTimestamp.Value, RtpTimestamp = header.Timestamp };
                }
            }
        });
    }
}
