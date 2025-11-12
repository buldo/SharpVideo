using System.Net;

namespace SharpVideo.RtpPlayerDemo.Rtp;

internal sealed class RtpSessionConfig
{
    /// <summary>
    /// If true only a single RTP socket will be used for both audio
    /// and video (standard case for WebRTC). If false two separate RTP sockets will be used for
    /// audio and video (standard case for VoIP).
    /// </summary>
    public bool IsMediaMultiplexed { get; set; }

    /// <summary>
    /// Optional. If specified this address will be used as the bind address for any RTP
    /// and control sockets created. Generally this address does not need to be set. The default behaviour
    /// is to bind to [::] or 0.0.0.0,d depending on system support, which minimises network routing
    /// causing connection issues.
    /// </summary>
    public IPAddress BindAddress { get; set; }

    /// <summary>
    /// Optional. If specified a single attempt will be made to bind the RTP socket
    /// on this port. It's recommended to leave this parameter as the default of 0 to let the Operating
    /// System select the port number.
    /// </summary>
    public int BindPort { get; set; }
}
