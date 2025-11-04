namespace SharpVideo.RtpPlayerDemo.Rtp;

internal class RTPPacket
{
    public RTPHeader Header;
    public byte[] Payload;

    public RTPPacket(byte[] packet)
    {
        Header = new RTPHeader(packet);
        Payload = new byte[Header.PayloadSize];
        Array.Copy(packet, Header.Length, Payload, 0, Payload.Length);
    }

    public byte[] GetBytes()
    {
        byte[] header = Header.GetBytes();
        byte[] packet = new byte[header.Length + Payload.Length];

        Array.Copy(header, packet, header.Length);
        Array.Copy(Payload, 0, packet, header.Length, Payload.Length);

        return packet;
    }

    private byte[] GetNullPayload(int numBytes)
    {
        byte[] payload = new byte[numBytes];

        for (int byteCount = 0; byteCount < numBytes; byteCount++)
        {
            payload[byteCount] = 0xff;
        }

        return payload;
    }
}
