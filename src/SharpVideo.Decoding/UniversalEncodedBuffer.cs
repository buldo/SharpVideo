namespace SharpVideo.Decoding;

public abstract class UniversalEncodedBuffer
{
    protected UniversalEncodedBuffer(int size)
    {
        Size = size;
    }

    public int Size { get; }
}