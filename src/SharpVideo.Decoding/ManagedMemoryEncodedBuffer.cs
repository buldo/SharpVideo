namespace SharpVideo.Decoding;

public class ManagedMemoryEncodedBuffer : UniversalEncodedBuffer
{
    private readonly byte[] _buffer;
    private int _nowUsed;

    public ManagedMemoryEncodedBuffer(int size) : base(size)
    {
        _buffer = GC.AllocateArray<byte>(size, true);
    }

    /// <summary>
    /// Only 3 or 4 start codes supported.
    /// We not check data
    /// </summary>
    public override void CopyFromSpan(ReadOnlySpan<byte> nalu)
    {
        var start = GetPayloadStart(nalu);
        nalu.CopyTo(_buffer);
        _nowUsed = nalu.Length;
        NaluPayloadStart = start;
    }

    public int NaluPayloadStart { get; private set; }

    public void AggregateInCurrent(List<ManagedMemoryEncodedBuffer> buffers)
    {
        _nowUsed = 0;

        foreach (var externalBuffer in buffers)
        {
            var sourceSpan = externalBuffer.Get();
            sourceSpan.CopyTo(_buffer.AsSpan(_nowUsed));
            _nowUsed += sourceSpan.Length;
        }
    }

    public Span<byte> Get()
    {
        return _buffer.AsSpan(0, _nowUsed);
    }
}