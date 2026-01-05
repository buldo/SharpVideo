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
        ReadOnlySpan<byte> fourByteStart = stackalloc byte[] { 0x00, 0x00, 0x00, 0x01 };
        return nalu.Length >= 4 && nalu.Slice(0, 4).SequenceEqual(fourByteStart) ? 4 : 3;
    }
}