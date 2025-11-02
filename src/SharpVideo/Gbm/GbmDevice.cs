using System.Runtime.Versioning;

using SharpVideo.Drm;
using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.Gbm;

namespace SharpVideo.Gbm;

[SupportedOSPlatform("linux")]
public class GbmDevice : IDisposable
{
    private readonly HashSet<GbmSurface> _surfaces = new();
    private nint _fd;
    private bool _disposed;

    private GbmDevice(nint gbmDeviceFd)
    {
        _fd = gbmDeviceFd;
    }

    public nint Fd => _fd;

    public static GbmDevice CreateFromDrmDevice(DrmDevice drmDevice)
    {
        var gbmDeviceFd = LibGbm.CreateDevice(drmDevice.DeviceFd);
        if (gbmDeviceFd == 0)
        {
            throw new Exception("Failed to create GBM device");
        }

        return new(gbmDeviceFd);
    }

    public GbmSurface CreateSurface(uint width, uint height, PixelFormat pixelFormat, GbmBoUse gbmBoUseRendering)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var gbmSurfaceFd = LibGbm.CreateSurface(_fd, width, height, pixelFormat.Fourcc, gbmBoUseRendering);
        if (gbmSurfaceFd == 0)
        {
            throw new Exception("Failed to create GBM surface");
        }

        var surface = new GbmSurface(gbmSurfaceFd);
        _surfaces.Add(surface);
        return surface;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources - cleanup all created surfaces
                foreach (var surface in _surfaces)
                {
                    surface.Dispose();
                }
                _surfaces.Clear();
            }

            // Dispose unmanaged resources - destroy GBM device
            if (_fd != 0)
            {
                LibGbm.DestroyDevice(_fd);
                _fd = 0;
            }

            _disposed = true;
        }
    }

    ~GbmDevice()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}