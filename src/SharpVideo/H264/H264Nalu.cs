namespace SharpVideo.H264;

public class H264Nalu
{
    private readonly byte[] _data;
    private readonly int _payloadStart;

    public H264Nalu(byte[] data, int payloadStart)
    {
        _data = data;
        _payloadStart = payloadStart;
    }

    public ReadOnlySpan<byte> Data => _data;
    public ReadOnlySpan<byte> WithoutHeader => _data.AsSpan(_payloadStart);
}