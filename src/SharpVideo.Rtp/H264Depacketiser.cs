namespace SharpVideo.Rtp;

public class H264Depacketiser
{
    const int SPS = 7;
    const int PPS = 8;
    const int NON_IDR_SLICE = 5;

    //Payload Helper Fields
    private readonly MemoryStream _fragmentedNal = new(); // used to concatenate fragmented H264 NALs where NALs are splitted over RTP packets
    private readonly List<KeyValuePair<int, byte[]>> _temporaryRtpPayloads = new List<KeyValuePair<int, byte[]>>(); // used to assemble the RTP packets that form one RTP Frame
    private readonly MemoryStream _outputBuffer = new();
    uint _previousTimestamp = 0;
    int norm, fu_a, fu_b, stap_a, stap_b, mtap16, mtap24 = 0; // used for diagnostics stats

    public virtual MemoryStream? ProcessRTPPayload(byte[] rtpPayload, ushort seqNum, uint timestamp, int markbit, out bool isKeyFrame)
    {
        List<ReadOnlyMemory<byte>>? nalUnits = ProcessRTPPayloadAsNals(rtpPayload, seqNum, timestamp, markbit, out isKeyFrame);

        if (nalUnits != null && nalUnits.Count > 0)
        {
            _outputBuffer.SetLength(0);
            
            foreach (var nal in nalUnits)
            {
                if (nal.Length == 0) continue;

                // Annex-B separator (00 00 00 01)
                _outputBuffer.WriteByte(0);
                _outputBuffer.WriteByte(0);
                _outputBuffer.WriteByte(0);
                _outputBuffer.WriteByte(1);
                _outputBuffer.Write(nal.Span);
            }
            
            return _outputBuffer;
        }
        return null;
    }

    private List<ReadOnlyMemory<byte>>? ProcessRTPPayloadAsNals(byte[] rtpPayload, ushort seqNum, uint timestamp, int markbit, out bool isKeyFrame)
    {
        return ProcessH264Payload(rtpPayload, seqNum, timestamp, markbit, out isKeyFrame);
    }

    private List<ReadOnlyMemory<byte>>? ProcessH264Payload(byte[] rtp_payload, ushort seqNum, uint rtp_timestamp, int rtp_marker, out bool isKeyFrame)
    {
        if (_previousTimestamp != rtp_timestamp && _previousTimestamp > 0)
        {
            _temporaryRtpPayloads.Clear();
            _previousTimestamp = 0;
            _fragmentedNal.SetLength(0);
        }

        // Add to the list of payloads for the current Frame of video
        _temporaryRtpPayloads.Add(new KeyValuePair<int, byte[]>(seqNum, rtp_payload)); // TODO could optimise this and go direct to Process Frame if just 1 packet in frame
        if (rtp_marker == 1)
        {
            //Reorder to prevent UDP incorrect package order
            if (_temporaryRtpPayloads.Count > 1)
            {
                _temporaryRtpPayloads.Sort((a, b) => {
                    // Detect wraparound of sequence to sort packets correctly (Assumption that no more then 2000 packets per frame)
                    return (Math.Abs(b.Key - a.Key) > (0xFFFF - 2000)) ? -a.Key.CompareTo(b.Key) : a.Key.CompareTo(b.Key);
                });
            }

            // End Marker is set. Process the list of RTP Packets (forming 1 RTP frame) and save the NALs to a file
            List<ReadOnlyMemory<byte>>? nal_units = ProcessH264PayloadFrame(_temporaryRtpPayloads, out isKeyFrame);
            _temporaryRtpPayloads.Clear();
            _previousTimestamp = 0;
            _fragmentedNal.SetLength(0);

            return nal_units;
        }
        else
        {
            isKeyFrame = false;
            _previousTimestamp = rtp_timestamp;
            return null; // we don't have a frame yet. Keep accumulating RTP packets
        }
    }

    // Process a RTP Frame. A RTP Frame can consist of several RTP Packets which have the same Timestamp
    // Returns a list of NAL Units (with no 00 00 00 01 header and with no Size header)
    protected virtual List<ReadOnlyMemory<byte>>? ProcessH264PayloadFrame(List<KeyValuePair<int, byte[]>> rtp_payloads, out bool isKeyFrame)
    {
        bool isKeyFrameDetected = false;
        List<ReadOnlyMemory<byte>> nalUnits = new List<ReadOnlyMemory<byte>>(); // Stores the NAL units for a Video Frame.

        for (int payload_index = 0; payload_index < rtp_payloads.Count; payload_index++)
        {
            var payload = rtp_payloads[payload_index].Value;
            if (payload.Length == 0) continue;

            // Examine the first byte (the NAL header)
            int nal_header_f_bit = (payload[0] >> 7) & 0x01;
            int nal_header_nri = (payload[0] >> 5) & 0x03;
            int nal_header_type = payload[0] & 0x1F;

            // If the Nal Header Type is in the range 1..23 this is a normal NAL (not fragmented)
            if (nal_header_type >= 1 && nal_header_type <= 23)
            {
                norm++;
                if (CheckKeyFrame(nal_header_type)) isKeyFrameDetected = true;
                nalUnits.Add(payload);
            }
            // There are 4 types of Aggregation Packet (split over RTP payloads)
            else if (nal_header_type == 24)
            {
                stap_a++;

                // RTP packet contains multiple NALs, each with a 16 bit header
                int ptr = 1; // start after the nal_header_type which was '24'
                while (ptr + 2 < payload.Length)
                {
                    int size = (payload[ptr] << 8) + payload[ptr + 1];
                    ptr += 2;
                    
                    if (ptr + size > payload.Length) break;

                    int reconstructed_nal_type = payload[ptr] & 0x1F;
                    if (CheckKeyFrame(reconstructed_nal_type)) isKeyFrameDetected = true;

                    nalUnits.Add(new ReadOnlyMemory<byte>(payload, ptr, size));
                    ptr += size;
                }
            }
            else if (nal_header_type == 28) // FU-A
            {
                fu_a++;

                if (payload.Length < 2) continue;

                // Parse Fragmentation Unit Header
                int fu_indicator = payload[0];
                int fu_header_s = (payload[1] >> 7) & 0x01;  // start marker
                int fu_header_e = (payload[1] >> 6) & 0x01;  // end marker
                int fu_header_type = payload[1] & 0x1F; // Original NAL unit header

                // Check Start and End flags
                if (fu_header_s == 1)
                {
                    // Start of Fragment.
                    byte reconstructed_nal_type = (byte)((nal_header_f_bit << 7) + (nal_header_nri << 5) + fu_header_type);

                    _fragmentedNal.SetLength(0);
                    _fragmentedNal.WriteByte(reconstructed_nal_type);
                    _fragmentedNal.Write(payload, 2, payload.Length - 2);
                }
                else if (fu_header_e == 1)
                {
                    // End part of Fragment
                    _fragmentedNal.Write(payload, 2, payload.Length - 2);

                    var fragmented_nal_array = _fragmentedNal.ToArray();
                    int reconstructed_nal_type = fragmented_nal_array[0] & 0x1F;
                    if (CheckKeyFrame(reconstructed_nal_type)) isKeyFrameDetected = true;

                    nalUnits.Add(fragmented_nal_array);
                    _fragmentedNal.SetLength(0);
                }
                else
                {
                    // Middle part of Fragment
                    _fragmentedNal.Write(payload, 2, payload.Length - 2);
                }
            }
            // Other types (STAP-B, MTAP16, MTAP24, FU-B) are ignored for simplicity as they are rare
        }

        isKeyFrame = isKeyFrameDetected;
        return nalUnits;
    }

    private bool CheckKeyFrame(int nalType)
    {
        return nalType == SPS || nalType == PPS;
    }
}
