using Microsoft.Extensions.Logging;
using SharpVideo.Drm;

namespace SharpVideo.Utils;

public static class DrmUtils
{
    /// <summary>
    /// Opens the first available DRM device from /dev/dri/card*.
    /// </summary>
    /// <returns>Opened DRM device or null if no devices available.</returns>
    public static DrmDevice? OpenDrmDevice(ILogger logger)
    {
        var devices = Directory.EnumerateFiles("/dev/dri", "card*", SearchOption.TopDirectoryOnly);
        foreach (var device in devices)
        {
            try
            {
                var drmDevice = DrmDevice.Open(device);
                if (drmDevice != null)
                {
                    logger.LogInformation("Opened DRM device: {Device}", device);
                    return drmDevice;
                }
                logger.LogWarning("Failed to open DRM device: {Device}", device);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Exception while opening DRM device: {Device}", device);
            }
        }
        return null;
    }
}