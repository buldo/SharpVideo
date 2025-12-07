using FFmpeg.AutoGen;

namespace SharpVideo.FfmpegDemo;

/// <summary>
/// Represents a decoded video frame from FFmpeg decoder
/// </summary>
internal unsafe class FfmpegVideoFrame
{
    public int Width { get; }
    public int Height { get; }
    public AVPixelFormat PixelFormat { get; }
    public long Pts { get; }
    public bool IsKeyFrame { get; }

    // YUV plane data (for most common formats like YUV420P, NV12)
    public byte[] PlaneY { get; }
    public byte[] PlaneU { get; }
    public byte[] PlaneV { get; }

    public int StrideY { get; }
    public int StrideU { get; }
    public int StrideV { get; }

    public FfmpegVideoFrame(AVFrame* frame)
    {
        Width = frame->width;
        Height = frame->height;
        PixelFormat = (AVPixelFormat)frame->format;
        Pts = frame->pts;
        IsKeyFrame = (frame->flags & ffmpeg.AV_FRAME_FLAG_KEY) != 0;

        StrideY = frame->linesize[0];
        StrideU = frame->linesize[1];
        StrideV = frame->linesize[2];

        // Copy Y plane
        int yPlaneSize = StrideY * Height;
        PlaneY = new byte[yPlaneSize];
        fixed (byte* destPtr = PlaneY)
        {
            Buffer.MemoryCopy(frame->data[0], destPtr, yPlaneSize, yPlaneSize);
        }

        // Copy U plane (half resolution for YUV420)
        int uvHeight = Height / 2;
        int uPlaneSize = StrideU * uvHeight;
        PlaneU = new byte[uPlaneSize];
        fixed (byte* destPtr = PlaneU)
        {
            Buffer.MemoryCopy(frame->data[1], destPtr, uPlaneSize, uPlaneSize);
        }

        // Copy V plane (half resolution for YUV420)
        int vPlaneSize = StrideV * uvHeight;
        PlaneV = new byte[vPlaneSize];
        fixed (byte* destPtr = PlaneV)
        {
            Buffer.MemoryCopy(frame->data[2], destPtr, vPlaneSize, vPlaneSize);
        }
    }
}
