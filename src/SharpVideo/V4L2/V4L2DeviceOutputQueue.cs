using System.Runtime.Versioning;

using SharpVideo.Linux.Native.V4L2;

namespace SharpVideo.V4L2;

[SupportedOSPlatform("linux")]
public class V4L2DeviceOutputQueue : V4L2DeviceQueue
{
    private MediaRequestsPool? _requestsPool;
    private MediaRequest?[] _associatedMediaRequests = new MediaRequest[256]; // Hope it will be enough

    internal V4L2DeviceOutputQueue(int deviceFd, V4L2BufferType type, Func<uint> planesCountAccessor) : base(deviceFd,
        type, planesCountAccessor)
    {
    }

    public void AssociateMediaRequests(IEnumerable<MediaRequest> requests)
    {
        _requestsPool = new MediaRequestsPool(requests.ToArray());
    }

    public MediaRequest AcquireMediaRequest()
    {
        if (_requestsPool == null)
        {
            throw new InvalidOperationException(
                "Media requests pool not initialized. Call AssociateMediaRequests first.");
        }

        return _requestsPool.Acquire();
    }

    public override void InitMMap(uint buffersCount)
    {
        base.InitMMap(buffersCount);
        _associatedMediaRequests = new MediaRequest[BuffersPool.Buffers.Count];
    }

    public void WriteBufferAndEnqueue(ReadOnlySpan<byte> data, MediaRequest? request = null, TimeVal? timestamp = null)
    {
        EnsureInitialised();
        var buffer = BuffersPool.AcquireBuffer();
        buffer.CopyDataToPlane(data, 0);
        _associatedMediaRequests[buffer.Index] = request;
        Enqueue(buffer, request, timestamp);
    }

    /// <summary>
    /// Enqueues an already filled mmap buffer to the OUTPUT queue.
    /// Use this for zero-copy scenarios where data is already in the buffer.
    /// </summary>
    /// <param name="buffer">The mmap buffer that already contains data.</param>
    /// <param name="request">Optional media request for stateless decoding.</param>
    /// <param name="timestamp">Optional timestamp for the buffer.</param>
    public void EnqueueBuffer(V4L2MMapMPlaneBuffer buffer, MediaRequest? request = null, TimeVal? timestamp = null)
    {
        EnsureInitialised();
        _associatedMediaRequests[buffer.Index] = request;
        Enqueue(buffer, request, timestamp);
    }

    public void ReclaimProcessed()
    {
        while (true)
        {
            var dequeuedBuffer = Dequeue();
            if (dequeuedBuffer == null)
            {
                // No more buffers available
                break;
            }

            BuffersPool.Release(dequeuedBuffer.Index);

            var mediaRequest = _associatedMediaRequests[dequeuedBuffer.Index];
            if (mediaRequest != null)
            {
                _associatedMediaRequests[dequeuedBuffer.Index] = null;
                _requestsPool?.Release(mediaRequest);
            }
        }
    }

    /// <summary>
    /// Reclaims processed buffers and invokes callback for each released buffer index.
    /// Use this to return V4l2EncodedBuffer wrappers to reuse queue.
    /// </summary>
    /// <param name="onBufferReleased">Callback invoked with buffer index when a buffer is released.</param>
    public void ReclaimProcessed(Action<uint> onBufferReleased)
    {
        while (true)
        {
            var dequeuedBuffer = Dequeue();
            if (dequeuedBuffer == null)
            {
                break;
            }

            BuffersPool.Release(dequeuedBuffer.Index);

            var mediaRequest = _associatedMediaRequests[dequeuedBuffer.Index];
            if (mediaRequest != null)
            {
                _associatedMediaRequests[dequeuedBuffer.Index] = null;
                _requestsPool?.Release(mediaRequest);
            }

            onBufferReleased(dequeuedBuffer.Index);
        }
    }

    /// <summary>
    /// Ensures that a free buffer is available in the pool by reclaiming processed buffers if needed.
    /// This method will block if all buffers are currently in use by hardware, actively polling
    /// until at least one buffer becomes available.
    /// </summary>
    public void EnsureFreeBuffer()
    {
        EnsureInitialised();

        // Keep reclaiming processed buffers until we have at least one free
        var spinWait = new SpinWait();
        while (!BuffersPool.HasFreeBuffer())
        {
            var dequeuedBuffer = Dequeue();
            if (dequeuedBuffer != null)
            {
                BuffersPool.Release(dequeuedBuffer.Index);

                var mediaRequest = _associatedMediaRequests[dequeuedBuffer.Index];
                if (mediaRequest != null)
                {
                    _associatedMediaRequests[dequeuedBuffer.Index] = null;
                    _requestsPool?.Release(mediaRequest);
                }
            }
            else
            {
                // No buffer ready yet, use SpinWait for efficient waiting
                spinWait.SpinOnce();
            }
        }
    }

    private class MediaRequestsPool
    {
        private readonly MediaRequest[] _requests;
        private readonly Queue<MediaRequest> _freeRequests;

        public MediaRequestsPool(MediaRequest[] requests)
        {
            _requests = requests;
            _freeRequests = new Queue<MediaRequest>(requests);
        }

        public MediaRequest Acquire()
        {
            lock (_freeRequests)
            {
                if (_freeRequests.Count > 0)
                {
                    return _freeRequests.Dequeue();
                }
            }

            throw new Exception("No free media requests");
        }

        public void Release(MediaRequest request)
        {
            request.ReInit();
            lock (_freeRequests)
            {
                _freeRequests.Enqueue(request);
            }
        }
    }
}
