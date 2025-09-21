using System.Runtime.InteropServices;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

public class V4L2Device : IDisposable
{
    private readonly int _deviceFd;
    private bool _disposed = false;

    internal V4L2Device(int deviceFd, List<V4L2DeviceControl> controls)
    {
        _deviceFd = deviceFd;
        Controls = controls.AsReadOnly();
    }

    public IReadOnlyCollection<V4L2DeviceControl> Controls { get; }

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