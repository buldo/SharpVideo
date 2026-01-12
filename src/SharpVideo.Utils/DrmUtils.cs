using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Drm;
using SharpVideo.Linux.Native;

namespace SharpVideo.Utils;

[SupportedOSPlatform("linux")]
public static class DrmUtils
{
    /// <summary>
    /// Opens the first available DRM device that has a connected display.
    /// </summary>
    /// <returns>Opened DRM device with connected display.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no DRM device with connected display is found.</exception>
    public static DrmDevice OpenDrmDevice(ILogger logger)
    {
        var devices = Directory.EnumerateFiles("/dev/dri", "card*", SearchOption.TopDirectoryOnly);
        foreach (var device in devices)
        {
            DrmDevice? drmDevice = null;
            try
            {
                drmDevice = DrmDevice.Open(device);
                if (drmDevice == null)
                {
                    logger.LogDebug("Failed to open DRM device: {Device}", device);
                    continue;
                }

                var resources = drmDevice.GetResources();
                if (resources == null)
                {
                    logger.LogDebug("Failed to get resources for DRM device: {Device}", device);
                    drmDevice.Dispose();
                    continue;
                }

                var hasConnectedDisplay = resources.Connectors
                    .Any(c => c.Connection == DrmModeConnection.Connected);

                if (hasConnectedDisplay)
                {
                    logger.LogInformation("Opened DRM device with connected display: {Device}", device);
                    return drmDevice;
                }

                logger.LogDebug("DRM device {Device} has no connected display, skipping", device);
                drmDevice.Dispose();
            }
            catch (DllNotFoundException ex)
            {
                logger.LogError(ex, "Native DRM library not found. Ensure that libdrm is installed.");
                drmDevice?.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Exception while opening DRM device: {Device}", device);
                drmDevice?.Dispose();
            }
        }

        throw new InvalidOperationException("Failed to find any DRM device with connected display");
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