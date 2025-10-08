using System.Runtime.Versioning;
using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

[SupportedOSPlatform("linux")]
public class V4L2DeviceOutputQueue : V4L2DeviceQueue
{
    private MediaRequestsPool _requestsPool;
    private MediaRequest?[] _associatedMediaRequests = new MediaRequest[256]; // Hope it will be enough

    internal V4L2DeviceOutputQueue(int deviceFd, V4L2BufferType type, Func<uint> planesCountAccessor) : base(deviceFd, type, planesCountAccessor)
    {
    }

    public void AssociateMediaRequests(IEnumerable<MediaRequest> requests)
    {
        _requestsPool = new MediaRequestsPool(requests.ToArray());
    }

    public MediaRequest AcquireMediaRequest()
    {
        return _requestsPool.Acquire();
    }

    public override void Init(V4L2Memory memory, uint buffersCount)
    {
        base.Init(memory, buffersCount);
        _associatedMediaRequests = new MediaRequest[BuffersPool.Buffers.Count];
    }

    public void WriteBufferAndEnqueue(ReadOnlySpan<byte> data, MediaRequest? request = null)
    {
        EnsureInitialised();
        var buffer = BuffersPool.AcquireBuffer();
        buffer.CopyDataToPlane(data, 0);
        Enqueue(buffer, request);
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
                _requestsPool.Release(mediaRequest);

            }
        }
    }

    private class MediaRequestsPool
    {
        private readonly Dictionary<MediaRequest, bool> _requests;

        public MediaRequestsPool(MediaRequest[] requests)
        {
            _requests = requests.ToDictionary(request => request, _ => false);
        }

        public MediaRequest Acquire()
        {
            foreach (var pair in _requests)
            {
                if (!pair.Value)
                {
                    _requests[pair.Key] = true;
                    return pair.Key;
                }
            }

            throw new Exception("No free media requests");
        }

        public void Release(MediaRequest request)
        {
            request.ReInit();
            _requests[request] = false;
        }
    }
}