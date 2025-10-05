using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

public record V4L2RequestedBuffers(V4L2BufferType Type, V4L2Memory Memory, uint Count);