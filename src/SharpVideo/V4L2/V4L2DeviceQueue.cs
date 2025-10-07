using System.Runtime.Versioning;
using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

[SupportedOSPlatform("linux")]
public class V4L2DeviceQueue
{
    private readonly V4L2Device _device;
    private readonly V4L2BufferType _type;
    private readonly V4L2Memory _memory;
    private readonly uint _planesCount;
    private V4L2RequestedBuffers _requestedBuffers;

    internal V4L2DeviceQueue(
        V4L2Device device,
        V4L2BufferType type,
        V4L2Memory memory,
        uint planesCount)
    {
        _device = device;
        _type = type;
        _memory = memory;
        _planesCount = planesCount;
    }

    public IReadOnlyList<V4L2MMapMPlaneBuffer> Buffers => _requestedBuffers.V4L2MMapMPlaneBuffers;

    public void Enqueue(V4L2MMapMPlaneBuffer mappedBuffer, MediaRequest? request = null)
    {
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

                var result = LibV4L2.QueueBuffer(_device.fd, ref buffer);
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
    /// <param name="planeCount">Number of planes for the buffer</param>
    /// <returns>Dequeued buffer with metadata, or null if no buffer available</returns>
    public DequeuedBuffer? Dequeue()
    {
        unsafe
        {
            var planeStorage = stackalloc V4L2Plane[(int)_planesCount];

            var buffer = new V4L2Buffer
            {
                Type = _type,
                Memory = V4L2Memory.MMAP,
                Length = _planesCount,
                Field = (uint)V4L2Field.NONE,
                Planes = planeStorage
            };

            var result = LibV4L2.DequeueBuffer(_device.fd, ref buffer);
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
            var planes = new V4L2Plane[_planesCount];
            for (int i = 0; i < _planesCount; i++)
            {
                planes[i] = planeStorage[i];
            }

            return new DequeuedBuffer
            {
                Index = buffer.Index,
                Planes = planes,
                Timestamp = buffer.Timestamp,
                Sequence = buffer.Sequence,
                Flags = buffer.Flags
            };
        }
    }

    /// <summary>
    /// Polls the device for events
    /// </summary>
    /// <param name="events">Events to poll for (POLLIN for capture, POLLOUT for output)</param>
    /// <param name="timeoutMs">Timeout in milliseconds</param>
    /// <returns>Poll result (>0 if events occurred, 0 if timeout, <0 if error) and returned events</returns>
    public (int result, PollEvents revents) Poll(int timeoutMs)
    {
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
            fd = _device.fd,
            events = events,
            revents = 0
        };

        var result = Libc.poll(ref pollFd, 1, timeoutMs);
        return (result, pollFd.revents);
    }

    public void RequestBuffers(uint count)
    {
        var reqBufs = new V4L2RequestBuffers
        {
            Count = count,
            Type = _type,
            Memory = _memory
        };

        var result = LibV4L2.RequestBuffers(_device.fd, ref reqBufs);
        if (!result.Success)
        {
            throw new Exception($"Failed to request {count} buffers with {_type} and {_memory}. {result.ErrorCode}: {result.ErrorMessage}");
        }



        V4L2MMapMPlaneBuffer[]? mmapMplanesArray = null;
        if ((_type == V4L2BufferType.VIDEO_CAPTURE_MPLANE || _type == V4L2BufferType.VIDEO_OUTPUT_MPLANE) &&
            _memory == V4L2Memory.MMAP)
        {
            mmapMplanesArray = new V4L2MMapMPlaneBuffer[reqBufs.Count];
        }

        unsafe
        {
            // 1. For each requested buffer
            for (uint i = 0; i < reqBufs.Count; i++)
            {
                var planes = new V4L2Plane[_planesCount];
                var buffer = new V4L2Buffer
                {
                    Index = i,
                    Type = _type,
                    Memory = _memory,
                    Length = _planesCount,
                    Field = (uint)V4L2Field.NONE,
                };

                fixed (V4L2Plane* planesPtr = planes)
                {
                    buffer.Planes = planesPtr;

                    // 2. We request buffer information
                    var queryResult = LibV4L2.QueryBuffer(_device.fd, ref buffer);
                    if (!queryResult.Success)
                    {
                        throw new Exception($"Failed to query buffer {i} for {_type} and {_memory}: {queryResult.ErrorMessage}");
                    }
                }

                // 3. And if buffer MPlane and mmap
                if ((_type == V4L2BufferType.VIDEO_CAPTURE_MPLANE || _type == V4L2BufferType.VIDEO_OUTPUT_MPLANE) &&
                    _memory == V4L2Memory.MMAP)
                {
                    var mappedPlanes = new List<V4L2MappedPlane>();
                    // 3.1 We are mapping each plane
                    foreach (var plane in planes)
                    {
                        var mapped = Libc.mmap(
                            IntPtr.Zero,
                            plane.Length,
                            ProtFlags.PROT_READ | ProtFlags.PROT_WRITE,
                            MapFlags.MAP_SHARED, _device.fd, (nint)plane.Memory.MemOffset);
                        if (mapped == Libc.MAP_FAILED)
                        {
                            throw new Exception("Failed to map buffer plane");
                        }
                        mappedPlanes.Add(new V4L2MappedPlane(mapped, plane.Length));
                    }

                    var bufferInfo = new V4L2MMapMPlaneBuffer(buffer, planes, mappedPlanes);

                    mmapMplanesArray![i] = bufferInfo;
                }
            }
        }

        // Here if buffer (MPlane && mmap), mmapMplanesArray contains buffer info with mapped planes
        _requestedBuffers = new V4L2RequestedBuffers(reqBufs.Type, reqBufs.Memory, reqBufs.Count, mmapMplanesArray);
    }

}