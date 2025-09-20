using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Linux.Native;
using SharpVideo.V4L2DecodeDemo.Interfaces;
using SharpVideo.V4L2DecodeDemo.Models;

namespace SharpVideo.V4L2DecodeDemo.Services;

/// <summary>
/// Manages V4L2 device discovery and capability enumeration
/// </summary>
[SupportedOSPlatform("linux")]
public class V4L2DeviceManager : IV4L2DeviceManager
{
    private readonly ILogger<V4L2DeviceManager> _logger;

    public V4L2DeviceManager(ILogger<V4L2DeviceManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Finds the first available H.264 decoder device
    /// </summary>
    public string? FindH264DecoderDevice()
    {
        _logger.LogInformation("Searching for H.264 capable V4L2 devices...");

        var devices = SharpVideo.V4L2.V4L2DeviceManager.GetH264Devices();
        var h264Device = devices.FirstOrDefault(d => d.IsMemoryToMemoryDevice);

        if (h264Device != null)
        {
            _logger.LogInformation("Found H.264 decoder device: {DevicePath} ({DriverName} - {CardName})",
                h264Device.DevicePath, h264Device.DriverName, h264Device.CardName);
            return h264Device.DevicePath;
        }

        _logger.LogWarning("No H.264 decoder device found");
        return null;
    }
}
