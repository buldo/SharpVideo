using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

public class V4L2RequestedBuffers
{
    private readonly V4L2MMapMPlaneBuffer[]? _v4L2MMapMPlaneBuffers;

    public V4L2RequestedBuffers(
        V4L2BufferType type,
        V4L2Memory memory,
        uint count,
        V4L2MMapMPlaneBuffer[]? v4L2MMapMPlaneBuffers)
    {
        Type = type;
        Memory = memory;
        Count = count;
        _v4L2MMapMPlaneBuffers = v4L2MMapMPlaneBuffers;
    }

    public V4L2BufferType Type { get; }

    public V4L2Memory Memory { get; }

    public uint Count { get; }

    public IReadOnlyList<V4L2MMapMPlaneBuffer>? V4L2MMapMPlaneBuffers => _v4L2MMapMPlaneBuffers;
}