using SharpVideo.H264;

namespace SharpVideo.Decoding.V4l2;

/// <summary>
/// Encoded buffer for V4L2 decoder input containing H264 NAL units.
/// </summary>
public class V4l2EncodedBuffer : UniversalEncodedBuffer
{
    private readonly byte[] _buffer;
    private int _usedBytes;

    /// <summary>
    /// Creates a new V4L2 encoded buffer with the specified size.
    /// </summary>
    /// <param name="size">Maximum buffer size in bytes.</param>
    public V4l2EncodedBuffer(int size) : base(size)
    {
        _buffer = GC.AllocateArray<byte>(size, pinned: true);
    }

    /// <summary>
    /// The offset in the buffer where NALU payload starts (after start code).
    /// </summary>
    public int NaluPayloadStart { get; private set; }

    /// <summary>
    /// The number of bytes currently used in the buffer.
    /// </summary>
    public int UsedBytes => _usedBytes;

    /// <summary>
    /// Copies data from a span into the buffer.
    /// Supports 3 or 4 byte start codes.
    /// </summary>
    public void CopyFromSpan(ReadOnlySpan<byte> nalu)
    {
        ReadOnlySpan<byte> fourByteStart = stackalloc byte[] { 0x00, 0x00, 0x00, 0x01 };
        var payloadStart = nalu.Length >= 4 && nalu.Slice(0, 4).SequenceEqual(fourByteStart) ? 4 : 3;

        nalu.CopyTo(_buffer);
        _usedBytes = nalu.Length;
        NaluPayloadStart = payloadStart;
    }

    /// <summary>
    /// Copies data from an H264Nalu into the buffer.
    /// </summary>
    public void CopyFromNalu(H264Nalu nalu)
    {
        nalu.Data.CopyTo(_buffer);
        _usedBytes = nalu.Data.Length;
        NaluPayloadStart = nalu.PayloadStart;
    }

    /// <summary>
    /// Gets the currently used portion of the buffer.
    /// </summary>
    public ReadOnlySpan<byte> GetData() => _buffer.AsSpan(0, _usedBytes);

    /// <summary>
    /// Gets the NALU payload (data after start code).
    /// </summary>
    public ReadOnlySpan<byte> GetPayload() => _buffer.AsSpan(NaluPayloadStart, _usedBytes - NaluPayloadStart);

    /// <summary>
    /// Resets the buffer for reuse.
    /// </summary>
    public void Reset()
    {
        _usedBytes = 0;
        NaluPayloadStart = 0;
    }
}
