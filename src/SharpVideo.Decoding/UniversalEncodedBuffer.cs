namespace SharpVideo.Decoding;

public abstract class UniversalEncodedBuffer
{
    protected UniversalEncodedBuffer(int size)
    {
        Size = size;
    }

    public int Size { get; }

    public abstract void CopyFromSpan(ReadOnlySpan<byte> nalu);

    protected int GetPayloadStart(ReadOnlySpan<byte> nalu)
    {
        // Check for 4-byte start code: 0x00 0x00 0x00 0x01
        if (nalu.Length >= 4 &&
            nalu[0] == 0x00 && nalu[1] == 0x00 && nalu[2] == 0x00 && nalu[3] == 0x01)
        {
            return 4;
        }

        // Check for 3-byte start code: 0x00 0x00 0x01
        if (nalu.Length >= 3 &&
            nalu[0] == 0x00 && nalu[1] == 0x00 && nalu[2] == 0x01)
        {
            return 3;
        }

        // No start code found - data starts at beginning
        return 0;
    }
}