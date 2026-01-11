using FFmpeg.AutoGen;

namespace SharpVideo.Decoding.Ffmpeg;

public unsafe class FfmpegDecodedFrame
{
    public FfmpegDecodedFrame(AVFrame* frame)
    {
        Frame = frame;
    }

    public AVFrame* Frame { get; }
}