namespace SharpVideo.Decoding;

public class ManagedMemoryEncodedBuffer : UniversalEncodedBuffer
{
    private readonly byte[] _buffer;
    private int _nowUsed;

    public ManagedMemoryEncodedBuffer(int size) : base(size)
    {
        _buffer = GC.AllocateArray<byte>(size, true);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        data.CopyTo(_buffer);
        _nowUsed = data.Length;
    }

    public Span<byte> Get()
    {
        return _buffer.AsSpan(0, _nowUsed);
    }
}