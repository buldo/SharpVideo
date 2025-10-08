using System.Runtime.Versioning;
using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

[SupportedOSPlatform("linux")]
public class V4L2DeviceCaptureQueue : V4L2DeviceQueue
{
    internal V4L2DeviceCaptureQueue(int deviceFd, V4L2BufferType type, Func<uint> planesCountAccessor) : base(deviceFd, type, planesCountAccessor)
    {
    }

    public DequeuedBuffer? WaitForReadyBuffer(int timeout)
    {
        var (pollResult, revents) = Poll(timeout);

        if (pollResult < 0)
        {
            // TODO: Error handling
            return null;
        }

        if (pollResult == 0)
        {
            // Timeout - check cancellation and continue
            return null;
        }

        // Check if there's data ready - if not, skip processing
        if (!revents.HasFlag(PollEvents.POLLIN))
        {
            return null;
        }

        return Dequeue();
    }

    public void ReuseBuffer(uint index)
    {
        var mappedBuffer = BuffersPool.Buffers[(int)index];
        for (int p = 0; p < mappedBuffer.Planes.Length; p++)
        {
            mappedBuffer.Planes[p].BytesUsed = 0;
        }

        Enqueue(mappedBuffer);
    }
}