using SharpVideo.H264;

namespace SharpVideo.Decoding.Ffmpeg;

internal class FfmpegH264FramesAggregator
{
    private readonly List<ManagedMemoryEncodedBuffer> _buffers = new();

    public bool AddBuffer(ManagedMemoryEncodedBuffer nalu)
    {
        _buffers.Add(nalu);
        var naluType = H264NalUnitParser.ParseTypeSimple(nalu.Get().Slice(nalu.NaluPayloadStart));
        if (naluType == NalUnitType.SPS_NUT || naluType == NalUnitType.PPS_NUT)
        {
            return false;
        }

        return true;
    }

    public List<ManagedMemoryEncodedBuffer> Drain()
    {
        var ret = new List<ManagedMemoryEncodedBuffer>(_buffers);
        _buffers.Clear();
        return ret;
    }
}