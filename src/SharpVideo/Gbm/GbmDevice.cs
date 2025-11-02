using SharpVideo.Drm;
using SharpVideo.MultiPlaneGlExample;

namespace SharpVideo.Gbm;

public class GbmDevice
{
    private readonly nint _gbmDeviceFd;

    private GbmDevice(nint gbmDeviceFd)
    {
        _gbmDeviceFd = gbmDeviceFd;
    }

    public static GbmDevice CreateFromDrmDevice(DrmDevice drmDevice)
    {
        var gbmDeviceFd = LibGbm.CreateDevice(drmDevice.DeviceFd);
        if (gbmDeviceFd == 0)
        {
            throw new Exception("Failed to create GBM device");
        }

        return new(gbmDeviceFd);
    }
}