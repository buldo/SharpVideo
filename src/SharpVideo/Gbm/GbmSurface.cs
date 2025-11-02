using System.Runtime.Versioning;
using SharpVideo.Linux.Native;

namespace SharpVideo.Gbm;

[SupportedOSPlatform("linux")]
public class GbmSurface : IDisposable
{
    private nint _gbmSurfaceFd;
    private bool _disposed;

    internal GbmSurface(nint gbmSurfaceFd)
    {
        _gbmSurfaceFd = gbmSurfaceFd;
    }

    public nint Fd => _gbmSurfaceFd;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources if any
            }

            // Dispose unmanaged resources
            if (_gbmSurfaceFd != 0)
            {
                LibGbm.DestroySurface(_gbmSurfaceFd);
                _gbmSurfaceFd = 0;
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