using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Threading;

using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

[SupportedOSPlatform("linux")]
public class V4L2QueueBufferPool
{
    private readonly V4L2MMapMPlaneBuffer[] _buffers;

    private readonly ConcurrentQueue<V4L2MMapMPlaneBuffer> _freeBuffers = new();
    private readonly AutoResetEvent _waitHandle = new(false);

    private V4L2QueueBufferPool(V4L2MMapMPlaneBuffer[] buffers, uint bufferPlaneCount)
    {
        _buffers = buffers;
        BufferPlaneCount = bufferPlaneCount;
    }

    public uint BufferPlaneCount { get; }

    public IReadOnlyList<V4L2MMapMPlaneBuffer> Buffers => _buffers;

    public static V4L2QueueBufferPool CreatePool(
        int deviceFd,
        uint buffersCount,
        V4L2BufferType bufferType,
        V4L2Memory memory,
        uint bufferPlaneCount)
    {
        var reqBufs = new V4L2RequestBuffers
        {
            Count = buffersCount,
            Type = bufferType,
            Memory = memory
        };

        var result = LibV4L2.RequestBuffers(deviceFd, ref reqBufs);
        if (!result.Success)
        {
            throw new Exception(
                $"Failed to request {buffersCount} buffers with {bufferType} and {memory}. {result.ErrorCode}: {result.ErrorMessage}");
        }

        var buffers = new V4L2MMapMPlaneBuffer[reqBufs.Count];

        // 1. For each requested buffer
        for (uint i = 0; i < reqBufs.Count; i++)
        {
            var planes = new V4L2Plane[bufferPlaneCount];
            var buffer = new V4L2Buffer
            {
                Index = i,
                Type = bufferType,
                Memory = memory,
                Length = bufferPlaneCount,
                Field = (uint)V4L2Field.NONE,
            };

            unsafe
            {
                fixed (V4L2Plane* planesPtr = planes)
                {
                    buffer.Planes = planesPtr;

                    // 2. We request buffer information
                    var queryResult = LibV4L2.QueryBuffer(deviceFd, ref buffer);
                    if (!queryResult.Success)
                    {
                        throw new Exception(
                            $"Failed to query buffer {i} for {bufferType} and {memory}: {queryResult.ErrorMessage}");
                    }
                }
            }

            buffers[i] = new V4L2MMapMPlaneBuffer(deviceFd, buffer, planes);
        }

        return new V4L2QueueBufferPool(buffers, bufferPlaneCount);
    }

    // IMHO, AcquireBuffer and SetBufferState - maybe should be in V4L2DeviceQueue

    internal V4L2MMapMPlaneBuffer AcquireBuffer()
    {
        // TODO: rewrite
        //while (!_disposed)
        while (true)
        {
            if (_freeBuffers.TryDequeue(out var item))
            {
                return item;
            }

            // Wait for signal that item is available
            if (!_waitHandle.WaitOne())
            {
                throw new TimeoutException("Dequeue operation timed out");
            }
        }

        throw new Exception("Failed to acquire buffer");
    }

    internal void Release(uint index)
    {
        var buffer = _buffers[index];
        _freeBuffers.Enqueue(buffer);
        _waitHandle.Set();
    }
}