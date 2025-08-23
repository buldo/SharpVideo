using SharpVideo.Linux.Native;

namespace SharpVideo.Drm;

public class DrmDevice
{
    private DrmDevice()
    {

    }

    public static DrmDevice? Open(string path)
    {
        int testFd = Libc.open(path, OpenFlags.O_RDWR);
        if (testFd < 0)
        {
            return null;
        }

        return new DrmDevice();
    }
}