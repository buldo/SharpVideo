using System.Runtime.Versioning;
using System.Threading.Tasks.Dataflow;

using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

[SupportedOSPlatform("linux")]
public class V4L2DeviceQueue
{
    private readonly int _deviceFd;
    private readonly V4L2BufferType _type;
    private readonly Func<uint> _planesCountAccessor;

    private bool _isInitialized;
    private V4L2QueueBufferPool? _buffersPool;

    internal V4L2DeviceQueue(
        int deviceFd,
        V4L2BufferType type,
        Func<uint> planesCountAccessor)
    {
        _deviceFd = deviceFd;
        _type = type;
        _planesCountAccessor = planesCountAccessor;
    }

    public V4L2QueueBufferPool BuffersPool => _isInitialized ? _buffersPool! : throw new Exception("Not initialised");

    public void Enqueue(V4L2MMapMPlaneBuffer mappedBuffer, MediaRequest? request = null)
    {
        EnsureInitialised();

        // To enqueue a buffer applications set the type field of a struct v4l2_buffer to the same buffer type as was previously used with struct v4l2_format type and struct v4l2_requestbuffers type.
        // Applications must also set the index field.
        // Valid index numbers range from zero to the number of buffers allocated with ioctl VIDIOC_REQBUFS (struct v4l2_requestbuffers count) minus one.
        // The contents of the struct v4l2_buffer returned by a ioctl VIDIOC_QUERYBUF ioctl will do as well.
        // When the buffer is intended for output (type is V4L2_BUF_TYPE_VIDEO_OUTPUT, V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE, or V4L2_BUF_TYPE_VBI_OUTPUT) applications must also initialize the bytesused, field and timestamp fields, see Buffers for details.
        // Applications must also set flags to 0.
        // The reserved2 and reserved fields must be set to 0.
        // When using the multi-planar API, the m.planes field must contain a userspace pointer to a filled-in array of struct v4l2_plane and the length field must be set to the number of elements in that array.

        // To enqueue a memory mapped buffer applications set the memory field to V4L2_MEMORY_MMAP.
        // When VIDIOC_QBUF is called with a pointer to this structure the driver sets the V4L2_BUF_FLAG_MAPPED and V4L2_BUF_FLAG_QUEUED flags and clears the V4L2_BUF_FLAG_DONE flag in the flags field, or it returns an EINVAL error code.
        var buffer = new V4L2Buffer
        {
            Index = mappedBuffer.Index,
            Type = _type,
            Memory = mappedBuffer.Memory,
            Length = (uint)mappedBuffer.MappedPlanes.Count,
            Field = (uint)V4L2Field.NONE,
            Flags = request != null ? (uint)V4L2BufferFlags.REQUEST_FD : 0,
            BytesUsed = 0,
            Timestamp = new TimeVal { TvSec = 0, TvUsec = 0 },
            Sequence = 0,
            RequestFd = request?.Fd ?? 0
        };

        unsafe
        {
            fixed (V4L2Plane* planePtr = mappedBuffer.Planes)
            {
                buffer.Planes = planePtr;

                var result = LibV4L2.QueueBuffer(_deviceFd, ref buffer);
                if (!result.Success)
                {
                    throw new Exception($"Failed to queue buffer for {_type}: {result.ErrorMessage ?? $"errno {result.ErrorCode}"}");
                }
            }
        }
    }

    /// <summary>
    /// Dequeues a buffer from the queue. Returns null if no buffer is available (EAGAIN).
    /// </summary>
    /// <returns>Dequeued buffer with metadata, or null if no buffer available</returns>
    public DequeuedBuffer? Dequeue()
    {
        EnsureInitialised();

        unsafe
        {
            var planeStorage = stackalloc V4L2Plane[(int)_buffersPool!.BufferPlaneCount];

            var buffer = new V4L2Buffer
            {
                Type = _type,
                Memory = V4L2Memory.MMAP,
                Length = _buffersPool.BufferPlaneCount,
                Field = (uint)V4L2Field.NONE,
                Planes = planeStorage
            };

            var result = LibV4L2.DequeueBuffer(_deviceFd, ref buffer);
            if (!result.Success)
            {
                if (result.ErrorCode == 11 || result.ErrorCode == 35) // EAGAIN or EWOULDBLOCK
                {
                    return null;
                }

                throw new Exception(
                    $"Failed to dequeue buffer from {_type}: {result.ErrorMessage ?? $"errno {result.ErrorCode}"}");
            }

            // Copy plane data from stack to managed array
            var planes = new V4L2Plane[_buffersPool.BufferPlaneCount];
            for (int i = 0; i < _buffersPool.BufferPlaneCount; i++)
            {
                planes[i] = planeStorage[i];
            }

            return new DequeuedBuffer
            {
                Index = buffer.Index,
                Planes = planes,
            };
        }
    }

    /// <summary>
    /// Polls the device for events
    /// </summary>
    /// <param name="timeoutMs">Timeout in milliseconds</param>
    /// <returns>Poll result (>0 if events occurred, 0 if timeout, <0 if error) and returned events</returns>
    public (int result, PollEvents revents) Poll(int timeoutMs)
    {
        EnsureInitialised();

        var events = _type switch
        {
            V4L2BufferType.VIDEO_CAPTURE => PollEvents.POLLIN,
            V4L2BufferType.VIDEO_CAPTURE_MPLANE => PollEvents.POLLIN,

            V4L2BufferType.VIDEO_OUTPUT => PollEvents.POLLOUT,
            V4L2BufferType.VIDEO_OUTPUT_OVERLAY => PollEvents.POLLOUT,

            _ => throw new Exception($"Type {_type} not supported")
        };

        var pollFd = new PollFd
        {
            fd = _deviceFd,
            events = events,
            revents = 0
        };

        var result = Libc.poll(ref pollFd, 1, timeoutMs);
        return (result, pollFd.revents);
    }

    /// <summary>
    /// Initialising queue.
    /// This method requesting buffer with specified type
    /// </summary>
    /// <param name="memory">Memory type</param>
    /// <param name="buffersCount">Buffers count</param>
    public virtual void Init(V4L2Memory memory, uint buffersCount)
    {
        if (_isInitialized)
        {
            throw new Exception("Already initialised");
        }

        _buffersPool = V4L2QueueBufferPool.CreatePool(_deviceFd, buffersCount, _type, memory, _planesCountAccessor());
        _isInitialized = true;
    }

    protected void EnsureInitialised()
    {
        if (!_isInitialized)
        {
            throw new Exception("Device queue not initialized");
        }
    }
}

[SupportedOSPlatform("linux")]
public class V4L2DeviceCaptureQueue : V4L2DeviceQueue
{
    internal V4L2DeviceCaptureQueue(int deviceFd, V4L2BufferType type, Func<uint> planesCountAccessor) : base(deviceFd, type, planesCountAccessor)
    {
    }
}