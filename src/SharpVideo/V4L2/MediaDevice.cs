using System.Runtime.Versioning;

using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

[SupportedOSPlatform("linux")]
public class MediaDevice : IDisposable
{
    private readonly int _deviceFd;
    private readonly Dictionary<int, MediaRequest> _requests = new();

    private bool _disposed = false;

    private MediaDevice(int deviceFd)
    {
        _deviceFd = deviceFd;
    }

    public static MediaDevice? Open(string path)
    {
        int deviceFd = Libc.open(path, OpenFlags.O_RDWR | OpenFlags.O_NONBLOCK);
        if (deviceFd < 0)
        {
            return null;
        }

        return new MediaDevice(deviceFd);
    }

    public void AllocateMediaRequests(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var (result, requestFd) = LibMedia.AllocateRequest(_deviceFd);
            if (!result.Success || requestFd < 0)
            {
                throw new Exception("Allocation of media request failed");
            }

            _requests.Add(requestFd, new MediaRequest(requestFd));
        }
    }

    public void CloseRequest(int requestFd)
    {
        var toClose = _requests[requestFd];
        _requests.Remove(requestFd);
        toClose.Dispose();
    }

    public IReadOnlyCollection<MediaRequest> OpenedRequests => _requests.Values;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources here if any
                // Currently no managed resources to dispose
            }

            if (_deviceFd >= 0)
            {
                try
                {
                    foreach (var request in _requests)
                    {
                        request.Value.Dispose();
                    }

                    _requests.Clear();

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
    ~MediaDevice()
    {
        Dispose(false);
    }
}