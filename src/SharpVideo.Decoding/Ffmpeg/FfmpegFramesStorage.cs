using FFmpeg.AutoGen;

namespace SharpVideo.Decoding.Ffmpeg;

public unsafe class FfmpegFramesStorage
{
    private readonly Dictionary<IntPtr, FfmpegDecodedFrame> _avFrameToWrapper = new();

    private readonly Dictionary<FfmpegDecodedFrame, IntPtr> _wrapperToAvFrame = new();

    public FfmpegFramesStorage(int count)
    {
        for (int i = 0; i < 3; i++)
        {
            var frame = ffmpeg.av_frame_alloc();
            if (frame == null)
            {
                throw new Exception("Failed to allocate frame");
            }

            var wrapper = new FfmpegDecodedFrame(frame);
            _wrapperToAvFrame[wrapper] = (IntPtr)frame;
            _avFrameToWrapper[(IntPtr)frame] = wrapper;
        }
    }

    public AVFrame* GetAvFrame(FfmpegDecodedFrame original)
    {
        return (AVFrame*)_wrapperToAvFrame[original];
    }

    public FfmpegDecodedFrame GetWrapperFrame(AVFrame* frame)
    {
        return _avFrameToWrapper[(IntPtr)frame];
    }

    public List<FfmpegDecodedFrame> GetAllWrappers()
    {
        return _wrapperToAvFrame.Keys.ToList();
    }
}