using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Drm;
using SharpVideo.Linux.Native;

namespace SharpVideo.Utils;

[SupportedOSPlatform("linux")]
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

    public static List<DrmClientCapability> EnableDrmCapabilities(this DrmDevice drmDevice, ILogger logger)
    {
        var capsToEnable = new[]
        {
            DrmClientCapability.DRM_CLIENT_CAP_UNIVERSAL_PLANES,
            DrmClientCapability.DRM_CLIENT_CAP_ATOMIC
        };

        logger.LogInformation("Enabling DRM client capabilities");
        List<DrmClientCapability> enabledCaps = new();
        foreach (var cap in capsToEnable)
        {
            if (drmDevice.TrySetClientCapability(cap, true, out var code))
            {
                logger.LogInformation("Enabled {Capability}", cap);
                enabledCaps.Add(cap);
            }
            else
            {
                logger.LogWarning("Failed to enable {Capability}: error {Code}", cap, code);
            }
        }

        return enabledCaps;
    }


}