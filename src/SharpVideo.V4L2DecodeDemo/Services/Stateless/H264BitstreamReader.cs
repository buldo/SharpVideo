namespace SharpVideo.V4L2DecodeDemo.Services.Stateless;

/// <summary>
/// Simple H.264 bitstream reader for parameter set parsing
/// </summary>
internal class H264BitstreamReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private int _bytePosition;
    private int _bitPosition;

    public int Position => _bytePosition * 8 + _bitPosition;

    public H264BitstreamReader(ReadOnlyMemory<byte> data)
    {
        _data = data;
        _bytePosition = 0;
        _bitPosition = 0;
    }

    public byte ReadByte()
    {
        if (_bitPosition != 0)
            throw new InvalidOperationException("Cannot read byte when not byte-aligned");

        if (_bytePosition >= _data.Length)
            throw new EndOfStreamException("Attempted to read past end of stream");

        return _data.Span[_bytePosition++];
    }

    public bool ReadBit()
    {
        if (_bytePosition >= _data.Length)
            throw new EndOfStreamException("Attempted to read past end of stream");

        var bit = (_data.Span[_bytePosition] >> (7 - _bitPosition)) & 1;
        _bitPosition++;

        if (_bitPosition == 8)
        {
            _bitPosition = 0;
            _bytePosition++;
        }

        return bit == 1;
    }

    public uint ReadBits(int count)
    {
        if (count <= 0) return 0;

        uint result = 0;
        for (int i = 0; i < count; i++)
        {
            if (!HasMoreData())
                throw new EndOfStreamException($"Not enough data to read {count} bits");

            result = (result << 1) | (ReadBit() ? 1u : 0u);
        }
        return result;
    }

    public uint ReadUEG()
    {
        int leadingZeroBits = 0;

        // Count leading zero bits
        while (HasMoreData() && !ReadBit())
        {
            leadingZeroBits++;
            if (leadingZeroBits > 32) // Prevent infinite loop
                throw new InvalidDataException("Invalid UE(v) encoding - too many leading zeros");
        }

        if (leadingZeroBits == 0)
            return 0;

        // Check if we have enough bits remaining
        if (!HasEnoughBitsRemaining(leadingZeroBits))
            throw new EndOfStreamException($"Not enough data to read UE(v) with {leadingZeroBits} leading zeros");

        return (1u << leadingZeroBits) - 1 + ReadBits(leadingZeroBits);
    }

    public int ReadSEG()
    {
        uint codeNum = ReadUEG();
        return (codeNum % 2 == 0) ? -(int)(codeNum / 2) : (int)((codeNum + 1) / 2);
    }

    public bool HasMoreData()
    {
        // Check if we have at least one more bit available
        return _bytePosition < _data.Length &&
               (_bytePosition < _data.Length - 1 || _bitPosition < 8);
    }

    private bool HasEnoughBitsRemaining(int bitsNeeded)
    {
        var remainingBits = (_data.Length - _bytePosition) * 8 - _bitPosition;
        return remainingBits >= bitsNeeded;
    }
}