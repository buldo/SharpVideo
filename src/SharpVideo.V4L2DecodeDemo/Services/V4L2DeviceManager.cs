using Microsoft.Extensions.Logging;
using SharpVideo.Linux.Native;
using SharpVideo.V4L2DecodeDemo.Interfaces;
using SharpVideo.V4L2DecodeDemo.Models;

namespace SharpVideo.V4L2DecodeDemo.Services;

/// <summary>
/// Manages V4L2 device discovery and capability enumeration
/// </summary>
public class V4L2DeviceManager : IV4L2DeviceManager
{
    private readonly ILogger<V4L2DeviceManager> _logger;
    private readonly DecoderConfiguration _configuration;

    private const uint H264_STANDARD = 0x34363248; // 'H264'
    private const uint H264_PARSED_SLICE_DATA = 0x34363253; // 'S264'

    public V4L2DeviceManager(ILogger<V4L2DeviceManager> logger, DecoderConfiguration? configuration = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? new DecoderConfiguration();
    }

    /// <summary>
    /// Finds the first available H.264 decoder device
    /// </summary>
    public async Task<string?> FindH264DecoderDeviceAsync()
    {
        _logger.LogInformation("Searching for H.264 capable V4L2 devices...");

        var devices = await GetAvailableDevicesAsync();
        var h264Device = devices.FirstOrDefault(d => d.SupportsH264 && d.IsMemoryToMemoryDevice);

        if (h264Device != null)
        {
            _logger.LogInformation("Found H.264 decoder device: {DevicePath} ({DriverName} - {CardName})",
                h264Device.DevicePath, h264Device.DriverName, h264Device.CardName);
            return h264Device.DevicePath;
        }

        _logger.LogWarning("No H.264 decoder device found");
        return null;
    }

    /// <summary>
    /// Gets all available V4L2 devices with their capabilities
    /// </summary>
    public async Task<IEnumerable<V4L2DeviceInfo>> GetAvailableDevicesAsync()
    {
        var devices = new List<V4L2DeviceInfo>();

        for (int i = 0; i < _configuration.MaxDeviceScan; i++)
        {
            var devicePath = $"/dev/video{i}";

            if (!File.Exists(devicePath))
                continue;

            var deviceInfo = await GetDeviceInfoAsync(devicePath);
            if (deviceInfo != null)
            {
                devices.Add(deviceInfo);
            }
        }

        _logger.LogInformation("Found {DeviceCount} V4L2 devices", devices.Count);
        return devices;
    }

    private async Task<V4L2DeviceInfo?> GetDeviceInfoAsync(string devicePath)
    {
        return await Task.Run(() =>
        {
            var fd = Libc.open(devicePath, OpenFlags.O_RDWR);
            if (fd < 0)
            {
                _logger.LogDebug("Cannot open device {DevicePath}", devicePath);
                return null;
            }

            try
            {
                // Query device capabilities
                var result = LibV4L2.QueryCapabilities(fd, out var caps);
                if (!result.Success)
                {
                    _logger.LogDebug("Cannot query capabilities for {DevicePath}: {Error}",
                        devicePath, result.ErrorMessage);
                    return null;
                }

                var driverName = caps.DriverString;
                var cardName = caps.CardString;
                var deviceCaps = (V4L2Capabilities)caps.DeviceCaps;

                _logger.LogDebug("Device {DevicePath}: {DriverName} - {CardName}",
                    devicePath, driverName, cardName);

                // Check if it's a memory-to-memory device
                bool isM2M = deviceCaps.HasFlag(V4L2Capabilities.VIDEO_M2M_MPLANE) ||
                            deviceCaps.HasFlag(V4L2Capabilities.VIDEO_M2M);

                if (!isM2M)
                {
                    _logger.LogDebug("Device {DevicePath} is not a memory-to-memory device", devicePath);
                }

                // Enumerate supported formats
                var formats = GetSupportedFormats(fd, devicePath);
                bool supportsH264 = formats.Any(f =>
                    f.PixelFormat == H264_STANDARD || f.PixelFormat == H264_PARSED_SLICE_DATA);

                if (supportsH264)
                {
                    _logger.LogInformation("Device {DevicePath} supports H.264 decoding", devicePath);
                }

                return new V4L2DeviceInfo
                {
                    DevicePath = devicePath,
                    DriverName = driverName,
                    CardName = cardName,
                    DeviceCapabilities = caps.DeviceCaps,
                    SupportedFormats = formats,
                    SupportsH264 = supportsH264,
                    IsMemoryToMemoryDevice = isM2M
                };
            }
            finally
            {
                Libc.close(fd);
            }
        });
    }

    private List<V4L2FormatInfo> GetSupportedFormats(int fd, string devicePath)
    {
        var formats = new List<V4L2FormatInfo>();

        _logger.LogDebug("Enumerating formats for {DevicePath}...", devicePath);

        for (uint fmtIndex = 0; fmtIndex < 32; fmtIndex++)
        {
            var fmtDesc = new V4L2FmtDesc
            {
                Index = fmtIndex,
                Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE
            };

            var result = LibV4L2.EnumerateFormat(fd, ref fmtDesc);
            if (!result.Success)
                break;

            var formatInfo = new V4L2FormatInfo
            {
                Index = fmtIndex,
                PixelFormat = fmtDesc.PixelFormat,
                Description = fmtDesc.DescriptionString,
                BufferType = V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE
            };

            formats.Add(formatInfo);

            _logger.LogDebug("  Format {Index}: {Description} (FOURCC: 0x{PixelFormat:X8})",
                fmtIndex, formatInfo.Description, formatInfo.PixelFormat);
        }

        return formats;
    }
}
