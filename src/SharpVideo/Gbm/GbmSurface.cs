using System.Runtime.Versioning;
using SharpVideo.Linux.Native;

namespace SharpVideo.Gbm;

[SupportedOSPlatform("linux")]
public class GbmSurface : IDisposable
{
    private nint _gbmSurfacePtr;
    private bool _disposed;

    internal GbmSurface(nint gbmSurfacePtr)
    {
        _gbmSurfacePtr = gbmSurfacePtr;
    }

    /// <summary>
    /// Gets the native GBM surface pointer (not a file descriptor).
    /// This pointer can be used with LibGbm functions and EGL.
    /// </summary>
    public nint Handle => _gbmSurfacePtr;

    /// <summary>
    /// [Obsolete] Use Handle instead. This property name is misleading as it's a pointer, not a file descriptor.
    /// </summary>
    [Obsolete("Use Handle property instead. This is a pointer to GBM surface structure, not a file descriptor.")]
    public nint Fd => _gbmSurfacePtr;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources if any
            }

            // Dispose unmanaged resources
            if (_gbmSurfacePtr != 0)
            {
                LibGbm.DestroySurface(_gbmSurfacePtr);
                _gbmSurfacePtr = 0;
            }

            _disposed = true;
        }
    }

    ~GbmSurface()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}