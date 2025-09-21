using System.Runtime.InteropServices;
using SharpVideo.Drm;

namespace SharpVideo.V4L2;

public class V4L2Device : IDisposable
{
    private readonly int _deviceFd;

    internal V4L2Device(int deviceFd, List<V4L2DeviceControl> controls)
    {
        _deviceFd = deviceFd;
        Controls = controls.AsReadOnly();
    }

    public IReadOnlyCollection<V4L2DeviceControl> Controls { get; }

    // TODO: Remove
    public int fd => _deviceFd;

    public void Dispose()
    {

    }
}