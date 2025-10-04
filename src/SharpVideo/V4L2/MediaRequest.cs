using System.Runtime.Versioning;
using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

[SupportedOSPlatform("linux")]
public class MediaRequest : IDisposable
{
    private readonly int _requestFd;
    private bool _disposed = false;

    internal MediaRequest(int requestFd)
    {
        _requestFd = requestFd;
    }

    internal int Fd => _requestFd;

    public void ReInit()
    {
        LibMedia.ReinitRequest(_requestFd);
    }

    public void Queue()
    {
        LibMedia.QueueRequest(_requestFd);
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

            // Dispose unmanaged resources
            if (_requestFd >= 0)
            {
                try
                {
                    Libc.close(_requestFd);
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

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~MediaRequest()
    {
        Dispose(false);
    }

    public override string ToString()
    {
        return _requestFd.ToString();
    }

    public override int GetHashCode()
    {
        return _requestFd.GetHashCode();
    }
}