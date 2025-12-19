namespace SharpVideo.RtpPlayerDemo.Rtp;

internal class MediaStreamTrack
{
    /// <summary>
    /// The value used in the RTP Synchronisation Source header field for media packets
    /// sent using this media stream.
    /// Be careful that the RTP Synchronisation Source header field should not be changed
    /// unless specific implementations require it. By default this value is chosen randomly,
    /// with the intent that no two synchronization sources within the same RTP session
    /// will have the same SSRC.
    /// </summary>
    public uint Ssrc { get; set; }

    /// <summary>
    /// The last seqnum received from the remote peer for this stream.
    /// </summary>
    public ushort LastRemoteSeqNum { get; internal set; }

    // <summary>
    ///  a=extmap - Mapping for RTP header extensions
    /// </summary>
    public Dictionary<int, RTPHeaderExtension> HeaderExtensions { get; }
}
