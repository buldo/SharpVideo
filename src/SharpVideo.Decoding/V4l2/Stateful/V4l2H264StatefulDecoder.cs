using Microsoft.Extensions.Logging;
using SharpVideo.Drm;

namespace SharpVideo.Decoding.V4l2.Stateful;

public class V4l2H264StatefulDecoder : BaseDecoder
{
    public V4l2H264StatefulDecoder(ILogger logger) : base(logger)
    {
    }

    public override void ReuseDecodedFrame(UniversalDecodedFrame decodedFrame)
    {
        throw new NotImplementedException();
    }

    protected override void ProcessEncodedDataBuffer(UniversalEncodedBuffer encodedBuffer)
    {
        throw new NotImplementedException();
    }

    protected override void FlushDecoder()
    {
        throw new NotImplementedException();
    }

    public override PixelFormat OutputPixelFormat { get; }
}