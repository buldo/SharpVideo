using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Drm;
using SharpVideo.V4L2;

namespace SharpVideo.Decoding.V4l2.Stateful;

/// <summary>
/// V4L2 stateful H264 decoder.
/// Used for hardware decoders that manage decoding state internally
/// (e.g., Qualcomm Venus).
/// </summary>
/// <remarks>
/// Stateful decoders accept raw H264 NAL units (with start codes) and handle
/// all parsing, DPB management, and reference picture handling internally.
/// This is simpler than stateless decoders but offers less control.
/// </remarks>
[SupportedOSPlatform("linux")]
public class V4l2H264StatefulDecoder : BaseDecoder<V4l2DecodedFrame>
{
    public V4l2H264StatefulDecoder(V4L2Device device, ILogger logger) : base(logger)
    {
    }

    /// <inheritdoc />
    public override void Decode(ReadOnlySpan<byte> nalu)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public override void ReuseDecodedFrame(V4l2DecodedFrame decodedFrame)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    protected override void FlushDecoder()
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public override PixelFormat OutputPixelFormat { get; }
}