using SharpVideo.V4L2;

namespace SharpVideo.V4L2DecodeDemo.Services;

internal class MediaRequestContext
{
    public MediaRequest Request { get; }

    public MediaRequestContext(MediaRequest request)
    {
        Request = request;
    }

    public uint BufferIndex { get; set; }
    public bool InUse { get; set; }

    public MediaRequestContext Reset()
    {
        BufferIndex = 0;
        InUse = false;
        Request.ReInit();
        return this;
    }
}