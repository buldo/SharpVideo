using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

[SupportedOSPlatform("linux")]
public class V4L2Device : IDisposable
{
    private readonly int _deviceFd;

    private readonly Dictionary<V4L2BufferType, V4L2DeviceQueue> _queues = new();
    private readonly Dictionary<(V4L2BufferType Type, V4L2Memory Memory), V4L2RequestedBuffers> _bufferInfos = new();

    private bool _disposed = false;

    internal V4L2Device(int deviceFd, List<V4L2DeviceControl> controls, List<V4L2DeviceExtendedControl> extendedControls)
    {
        _deviceFd = deviceFd;
        Controls = controls.AsReadOnly();
        ExtendedControls = extendedControls.AsReadOnly();
    }

    public IReadOnlyCollection<V4L2DeviceControl> Controls { get; }

    public IReadOnlyCollection<V4L2DeviceExtendedControl> ExtendedControls { get; }

    public IReadOnlyCollection<V4L2RequestedBuffers> RequestedBufferInfos => _bufferInfos.Values;

    public V4L2DeviceQueue GetQueue(V4L2BufferType queue)
    {
        if (!_queues.TryGetValue(queue, out var value))
        {
            value = new V4L2DeviceQueue(_deviceFd, queue);
            _queues.Add(queue, value);
        }

        return value;
    }

    public void SetFormat(ref V4L2Format format)
    {
        var result = LibV4L2.SetFormat(_deviceFd, ref format);
        if (!result.Success)
        {
            throw new Exception(
                $"Failed to set format {format.Type}. {result.ErrorCode}: {result.ErrorMessage}");
        }
    }

    public void GetFormat(ref V4L2Format format)
    {
        var result = LibV4L2.GetFormat(fd, ref format);
        if (!result.Success)
        {
            throw new Exception(
                $"Failed to get format {format.Type}: {result.ErrorCode}: {result.ErrorMessage}");
        }
    }

    public void StreamOn(V4L2BufferType type)
    {
        var outputResult = LibV4L2.StreamOn(_deviceFd, type);
        if (!outputResult.Success)
        {
            throw new Exception($"Failed to start {type} streaming: {outputResult.ErrorMessage}");
        }
    }

    public void StreamOff(V4L2BufferType type)
    {
        var outputResult = LibV4L2.StreamOff(_deviceFd, type);
        if (!outputResult.Success)
        {
            throw new Exception($"Failed to start {type} streaming: {outputResult.ErrorMessage}");
        }
    }

    /// <summary>
    /// Try to set a simple V4L2 control
    /// </summary>
    public bool TrySetSimpleControl(uint controlId, int value)
    {

        var control = new V4L2Control
        {
            Id = controlId,
            Value = value
        };

        var result = LibV4L2.SetControl(_deviceFd, ref control);
        if (result.Success)
        {
            //_logger.LogInformation("Set {Description}", description);
            return true;
        }
        else
        {
            //_logger.LogWarning("Failed to set {Description}: {Error}", description, result.ErrorMessage);
            return false;
        }
    }


    /// <summary>
    /// Set a single extended control - much simpler and more predictable
    /// </summary>
    public void SetSingleExtendedControl<T>(uint controlId, T data, MediaRequest? request = null) where T : struct
    {
        ThrowIfDisposed();

        var size = (uint)Marshal.SizeOf<T>();
        var dataPtr = Marshal.AllocHGlobal((int)size);

        try
        {
            unsafe
            {
                new Span<byte>((void*)dataPtr, (int)size).Clear();
            }

            Marshal.StructureToPtr(data, dataPtr, false);

            var control = new V4L2ExtControl
            {
                Id = controlId,
                Size = size,
                Ptr = dataPtr
            };

            var controlPtr = Marshal.AllocHGlobal(Marshal.SizeOf<V4L2ExtControl>());

            try
            {
                Marshal.StructureToPtr(control, controlPtr, false);
                var requestFd = request?.Fd ?? -1;
                var extControlsWrapper = new V4L2ExtControls
                {
                    Which = GetControlClass(controlId),
                    Count = 1,
                    RequestFd = requestFd,
                    Controls = controlPtr
                };

                var result = LibV4L2.SetExtendedControls(_deviceFd, ref extControlsWrapper);
                if (!result.Success)
                {
                    var errorCode = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"Failed to set control 0x{controlId:X8}: {result.ErrorMessage} (errno: {errorCode})");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(controlPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(dataPtr);
        }
    }

    public V4L2RequestedBuffers RequestBuffers(uint count, V4L2BufferType type, V4L2Memory memory)
    {
        var reqBufs = new V4L2RequestBuffers
        {
            Count = count,
            Type = type,
            Memory = memory
        };

        var result = LibV4L2.RequestBuffers(_deviceFd, ref reqBufs);
        if (!result.Success)
        {
            throw new Exception($"Failed to request {count} buffers with {type} and {memory}. {result.ErrorCode}: {result.ErrorMessage}");
        }

        var format = new V4L2Format
        {
            Type = type
        };

        GetFormat(ref format);

        V4L2MMapMPlaneBuffer[]? mmapMplanesArray = null;
        if ((type == V4L2BufferType.VIDEO_CAPTURE_MPLANE || type == V4L2BufferType.VIDEO_OUTPUT_MPLANE) &&
            memory == V4L2Memory.MMAP)
        {
            mmapMplanesArray = new V4L2MMapMPlaneBuffer[reqBufs.Count];
        }

        unsafe
        {
            // 1. For each requested buffer
            for (uint i = 0; i < reqBufs.Count; i++)
            {
                var planes = new V4L2Plane[format.Pix_mp.NumPlanes];
                var buffer = new V4L2Buffer
                {
                    Index = i,
                    Type = type,
                    Memory = memory,
                    Length = format.Pix_mp.NumPlanes,
                    Field = (uint)V4L2Field.NONE,
                };

                fixed (V4L2Plane* planesPtr = planes)
                {
                    buffer.Planes = planesPtr;

                    // 2. We request buffer information
                    var queryResult = LibV4L2.QueryBuffer(_deviceFd, ref buffer);
                    if (!queryResult.Success)
                    {
                        throw new Exception($"Failed to query buffer {i} for {type} and {memory}: {queryResult.ErrorMessage}");
                    }
                }

                // 3. And if buffer MPlane and mmap
                if ((type == V4L2BufferType.VIDEO_CAPTURE_MPLANE || type == V4L2BufferType.VIDEO_OUTPUT_MPLANE) &&
                    memory == V4L2Memory.MMAP)
                {
                    var mappedPlanes = new List<V4L2MappedPlane>();
                    // 3.1 We are mapping each plane
                    foreach (var plane in planes)
                    {
                        var mapped = Libc.mmap(
                            IntPtr.Zero,
                            plane.Length,
                            ProtFlags.PROT_READ | ProtFlags.PROT_WRITE,
                            MapFlags.MAP_SHARED, _deviceFd, (nint)plane.Memory.MemOffset);
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
        var info = new V4L2RequestedBuffers(reqBufs.Type, reqBufs.Memory, reqBufs.Count, mmapMplanesArray);
        _bufferInfos[(info.Type, info.Memory)] = info;
        return info;
    }

    private static uint GetControlClass(uint controlId)
    {
        // Mirror V4L2_CTRL_ID2CLASS behaviour while masking out NEXT_CTRL flag.
        const uint nextCtrlFlag = 0x80000000u;
        const uint classMask = 0xFFFF0000u;

        var normalizedId = controlId & ~nextCtrlFlag;
        var controlClass = normalizedId & classMask;

        return controlClass != 0 ? controlClass : V4l2ControlsConstants.V4L2_CTRL_CLASS_USER;
    }

    // TODO: Remove
    public int fd
    {
        get
        {
            ThrowIfDisposed();
            return _deviceFd;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the device has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Releases all resources used by the V4L2Device.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the V4L2Device and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources here if any
                // Currently no managed resources to dispose
            }

            // Dispose unmanaged resources
            if (_deviceFd >= 0)
            {
                try
                {
                    Libc.close(_deviceFd);
                }
                catch
                {
                    // Ignore exceptions during disposal to prevent resource leaks
                    // Log if logging is available in the future
                }
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Finalizer that ensures the file descriptor is closed if Dispose was not called.
    /// </summary>
    ~V4L2Device()
    {
        Dispose(false);
    }

    /// <summary>
    /// Throws an ObjectDisposedException if the object has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(V4L2Device));
        }
    }
}

[SupportedOSPlatform("linux")]
public class V4L2DeviceQueue
{
    private readonly int _deviceFd;
    private readonly V4L2BufferType _type;

    internal V4L2DeviceQueue(int deviceFd, V4L2BufferType type)
    {
        _deviceFd = deviceFd;
        _type = type;
    }

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
    /// <param name="planeCount">Number of planes for the buffer</param>
    /// <returns>Dequeued buffer with metadata, or null if no buffer available</returns>
    public DequeuedBuffer? Dequeue(int planeCount)
    {
        unsafe
        {
            var planeStorage = stackalloc V4L2Plane[planeCount];

            var buffer = new V4L2Buffer
            {
                Type = _type,
                Memory = V4L2Memory.MMAP,
                Length = (uint)planeCount,
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
            var planes = new V4L2Plane[planeCount];
            for (int i = 0; i < planeCount; i++)
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
    public (int result, PollEvents revents) Poll(PollEvents events, int timeoutMs)
    {
        var pollFd = new PollFd
        {
            fd = _deviceFd,
            events = events,
            revents = 0
        };

        unsafe
        {
            var result = Libc.poll(ref pollFd, 1, timeoutMs);
            return (result, pollFd.revents);
        }
    }
}

/// <summary>
/// Represents a dequeued buffer with metadata
/// </summary>
[SupportedOSPlatform("linux")]
public class DequeuedBuffer
{
    public uint Index { get; init; }
    public V4L2Plane[] Planes { get; init; } = Array.Empty<V4L2Plane>();
    public TimeVal Timestamp { get; init; }
    public uint Sequence { get; init; }
    public uint Flags { get; init; }

    public uint TotalBytesUsed
    {
        get
        {
            uint total = 0;
            foreach (var plane in Planes)
            {
                total += plane.BytesUsed;
            }
            return total;
        }
    }
}