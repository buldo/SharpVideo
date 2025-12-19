using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SharpVideo.RtpPlayerDemo.Rtp;

internal class RtpVideoFramer
{
    private static ILogger logger = new NullLogger<RtpVideoFramer>();

    private readonly VideoCodecsEnum _codec;
    private readonly int _maxFrameSize;
    private readonly byte[] _currentVideoFrame;
    private int _currVideoFramePosn = 0;
    private H264Depacketiser? _h264Depacketiser;

    public RtpVideoFramer(VideoCodecsEnum codec, int maxFrameSize)
    {
        if (!(codec == VideoCodecsEnum.VP8 || codec == VideoCodecsEnum.H264))
        {
            throw new NotSupportedException("The RTP video framer currently only understands H264 and VP8 encoded frames.");
        }

        _codec = codec;
        _maxFrameSize = maxFrameSize;
        _currentVideoFrame = new byte[maxFrameSize];

        if (_codec == VideoCodecsEnum.H264)
        {
            _h264Depacketiser = new H264Depacketiser();
        }
    }

    public byte[]? GotRtpPacket(RTPPacket rtpPacket)
    {
        var payload = rtpPacket.Payload;

        var hdr = rtpPacket.Header;

        if (_codec == VideoCodecsEnum.VP8)
        {
            //logger.LogDebug($"rtp VP8 video, seqnum {hdr.SequenceNumber}, ts {hdr.Timestamp}, marker {hdr.MarkerBit}, payload {payload.Length}.");

            if (_currVideoFramePosn + payload.Length >= _maxFrameSize)
            {
                // Something has gone very wrong. Clear the buffer.
                _currVideoFramePosn = 0;
            }

            // New frames must have the VP8 Payload Descriptor Start bit set.
            // The tracking of the current video frame position is to deal with a VP8 frame being split across multiple RTP packets
            // as per https://tools.ietf.org/html/rfc7741#section-4.4.
            if (_currVideoFramePosn > 0 || (payload[0] & 0x10) > 0)
            {
                RtpVP8Header vp8Header = RtpVP8Header.GetVP8Header(payload);

                Buffer.BlockCopy(payload, vp8Header.Length, _currentVideoFrame, _currVideoFramePosn, payload.Length - vp8Header.Length);
                _currVideoFramePosn += payload.Length - vp8Header.Length;

                if (rtpPacket.Header.MarkerBit > 0)
                {
                    var frame = _currentVideoFrame.Take(_currVideoFramePosn).ToArray();

                    _currVideoFramePosn = 0;

                    return frame;
                }
            }
            else
            {
                logger.LogWarning("Discarding RTP packet, VP8 header Start bit not set.");
                //logger.LogWarning($"rtp video, seqnum {hdr.SequenceNumber}, ts {hdr.Timestamp}, marker {hdr.MarkerBit}, payload {payload.Length}.");
            }
        }
        else if (_codec == VideoCodecsEnum.H264)
        {
            //logger.LogDebug($"rtp H264 video, seqnum {hdr.SequenceNumber}, ts {hdr.Timestamp}, marker {hdr.MarkerBit}, payload {payload.Length}.");

            //var hdr = rtpPacket.Header;
            var frameStream = _h264Depacketiser!.ProcessRTPPayload(payload, hdr.SequenceNumber, hdr.Timestamp, hdr.MarkerBit, out bool isKeyFrame);

            if (frameStream != null)
            {
                return frameStream.ToArray();
            }
        }
        else
        {
            logger.LogWarning($"rtp unknown video, seqnum {hdr.SequenceNumber}, ts {hdr.Timestamp}, marker {hdr.MarkerBit}, payload {payload.Length}.");
        }

        return null;
    }
}
