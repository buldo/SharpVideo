using FFmpeg.AutoGen;

namespace SharpVideo.Decoding;

public unsafe class FfmpegDecodedFrame : UniversalDecodedFrame
{
    public FfmpegDecodedFrame(AVFrame* frame)
    {
        Frame = frame;
    }

    public AVFrame* Frame { get; }
}