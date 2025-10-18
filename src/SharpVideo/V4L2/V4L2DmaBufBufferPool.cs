using System.Collections.Concurrent;
using System.Runtime.Versioning;
using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

/// <summary>
/// Buffer pool for V4L2 queues using DMABUF (external DMA buffers).
/// </summary>
[SupportedOSPlatform("linux")]
public class V4L2DmaBufBufferPool
{
    private readonly V4L2DmaBufMPlaneBuffer[] _buffers;
    private readonly ConcurrentQueue<V4L2DmaBufMPlaneBuffer> _freeBuffers = new();
    private readonly AutoResetEvent _waitHandle = new(false);

    private V4L2DmaBufBufferPool(V4L2DmaBufMPlaneBuffer[] buffers, uint bufferPlaneCount)
    {
        _buffers = buffers;
        BufferPlaneCount = bufferPlaneCount;

        foreach (var buffer in _buffers)
        {
            _freeBuffers.Enqueue(buffer);
        }
    }

    public uint BufferPlaneCount { get; }

    public IReadOnlyList<V4L2DmaBufMPlaneBuffer> Buffers => _buffers;

    public static V4L2DmaBufBufferPool CreatePool(
        int deviceFd,
        V4L2BufferType bufferType,
        int[][] dmaBufferFds, // Array of FD arrays (one FD array per buffer)
        uint[] planeSizes,
        uint[] planeOffsets)
    {
        uint bufferCount = (uint)dmaBufferFds.Length;
        uint planeCount = (uint)planeSizes.Length;

        // Validate that each buffer has the correct number of plane FDs
        // Note: For contiguous buffers (e.g., NV12 with numPlanes=1), the same FD
        // may be used for multiple logical planes with different offsets
        foreach (var bufferFds in dmaBufferFds)
        {
            if (bufferFds.Length != planeCount)
            {
                throw new ArgumentException($"Each buffer must have {planeCount} FD entries (one per logical plane). " +
                    $"For contiguous buffers, the same FD can be repeated with different offsets.");
            }
        }

        if (planeSizes.Length != planeOffsets.Length)
        {
            throw new ArgumentException($"Number of plane sizes ({planeSizes.Length}) must match number of plane offsets ({planeOffsets.Length})");
        }

        // Request buffers with DMABUF memory type
        var reqBufs = new V4L2RequestBuffers
        {
            Count = bufferCount,
            Type = bufferType,
            Memory = V4L2Memory.DMABUF
        };

        var result = LibV4L2.RequestBuffers(deviceFd, ref reqBufs);
        if (!result.Success)
        {
            throw new Exception(
                $"Failed to request {bufferCount} DMABUF buffers for {bufferType}. {result.ErrorCode}: {result.ErrorMessage}");
        }

        var buffers = new V4L2DmaBufMPlaneBuffer[bufferCount];

        for (uint i = 0; i < bufferCount; i++)
        {
            buffers[i] = new V4L2DmaBufMPlaneBuffer(i, dmaBufferFds[i], planeSizes, planeOffsets);
        }

        return new V4L2DmaBufBufferPool(buffers, planeCount);
    }

    public V4L2DmaBufMPlaneBuffer AcquireBuffer()
    {
        V4L2DmaBufMPlaneBuffer? buffer;
        while (!_freeBuffers.TryDequeue(out buffer))
        {
            _waitHandle.WaitOne();
        }

        return buffer;
    }

    public void SetBufferState(V4L2DmaBufMPlaneBuffer buffer, bool isFree)
    {
        if (isFree)
        {
            _freeBuffers.Enqueue(buffer);
            _waitHandle.Set();
        }
    }
}
