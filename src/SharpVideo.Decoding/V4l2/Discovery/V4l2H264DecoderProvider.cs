using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.C;
using SharpVideo.Linux.Native.V4L2;

namespace SharpVideo.Decoding.V4l2.Discovery;

/// <summary>
/// Service for discovering V4L2 H264 hardware decoders.
/// </summary>
[SupportedOSPlatform("linux")]
public class V4l2H264DecoderProvider
{
    private readonly ILogger<V4l2H264DecoderProvider> _logger;

    public V4l2H264DecoderProvider(ILogger<V4l2H264DecoderProvider> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Discovers all available V4L2 H264 decoders on the system.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    /// <returns>List of discovered decoder information.</returns>
    public IReadOnlyList<V4l2H264DecoderInfo> DiscoverDecoders()
    {
        var results = new List<V4l2H264DecoderInfo>();

        // Scan /dev/video* devices
        if (!Directory.Exists("/dev"))
        {
            _logger.LogTrace("No /dev directory found, skipping V4L2 discovery");
            return results;
        }

        var videoDevices = Directory.GetFiles("/dev", "video*")
            .OrderBy(p => p)
            .ToList();

        _logger.LogTrace("Found {Count} video devices to scan", videoDevices.Count);

        foreach (var devicePath in videoDevices)
        {
            var info = TryProbeDevice(devicePath);
            if (info != null)
            {
                results.Add(info);
                _logger.LogInformation(
                    "Discovered {DecoderType} H264 decoder at {Path} ({Driver}: {Card})",
                    info.DecoderType,
                    info.DevicePath,
                    info.Driver,
                    info.Card);
            }
        }

        return results;
    }

    /// <summary>
    /// Finds the best available H264 decoder, preferring hardware over software.
    /// Priority: Stateless > Stateful > None.
    /// </summary>
    /// <returns>The best decoder info, or null if no hardware decoder found.</returns>
    public V4l2H264DecoderInfo? FindBestDecoder()
    {
        var decoders = DiscoverDecoders();

        // Prefer stateless decoders (more control, lower latency potential)
        var stateless = decoders.FirstOrDefault(d => d.DecoderType == V4l2H264DecoderType.Stateless);
        if (stateless != null)
        {
            return stateless;
        }

        // Fall back to stateful
        return decoders.FirstOrDefault(d => d.DecoderType == V4l2H264DecoderType.Stateful);
    }

    private V4l2H264DecoderInfo? TryProbeDevice(string devicePath)
    {
        int fd = Libc.open(devicePath, OpenFlags.O_RDWR | OpenFlags.O_NONBLOCK);
        if (fd < 0)
        {
            _logger.LogTrace("Cannot open {Path}", devicePath);
            return null;
        }

        try
        {
            // Query capabilities
            var capResult = LibV4L2.QueryCapabilities(fd, out var capability);
            if (!capResult.Success)
            {
                _logger.LogTrace("Cannot query capabilities for {Path}", devicePath);
                return null;
            }

            // Check if it's a M2M device (decoder)
            var caps = capability.Capabilities.HasFlag(V4L2Capabilities.DEVICE_CAPS)
                ? capability.DeviceCaps
                : capability.Capabilities;

            // Must be a memory-to-memory device with multiplane support
            bool isM2M = caps.HasFlag(V4L2Capabilities.VIDEO_M2M_MPLANE) ||
                         (caps.HasFlag(V4L2Capabilities.VIDEO_CAPTURE_MPLANE) &&
                          caps.HasFlag(V4L2Capabilities.VIDEO_OUTPUT_MPLANE));

            if (!isM2M)
            {
                _logger.LogTrace("{Path} is not a M2M device", devicePath);
                return null;
            }

            // Check if it supports H264 input format
            if (!IsDeviceSupportsH264Input(fd))
            {
                _logger.LogTrace("{Path} does not support H264 input", devicePath);
                return null;
            }

            // Determine if stateless or stateful
            var decoderType = DetermineDecoderType(fd);
            if (decoderType == V4l2H264DecoderType.None)
            {
                _logger.LogTrace("{Path} is not an H264 decoder", devicePath);
                return null;
            }

            // Try to find associated media device for stateless decoders
            string? mediaDevicePath = null;
            if (decoderType == V4l2H264DecoderType.Stateless)
            {
                mediaDevicePath = TryFindMediaDevice(capability.BusInfoString);
            }

            return new V4l2H264DecoderInfo
            {
                DevicePath = devicePath,
                DecoderType = decoderType,
                Driver = capability.DriverString,
                Card = capability.CardString,
                MediaDevicePath = mediaDevicePath
            };
        }
        finally
        {
            Libc.close(fd);
        }
    }

    private bool IsDeviceSupportsH264Input(int fd)
    {
        // Check OUTPUT_MPLANE formats for H264 support
        var fmtDesc = new V4L2FmtDesc
        {
            Index = 0,
            Type = V4L2BufferType.VIDEO_OUTPUT_MPLANE
        };

        while (LibV4L2.EnumerateFormat(fd, ref fmtDesc).Success)
        {
            // Check for H264 formats (stateful uses H264, stateless uses H264_SLICE)
            if (fmtDesc.PixelFormat == V4L2PixelFormats.V4L2_PIX_FMT_H264 ||
                fmtDesc.PixelFormat == V4L2PixelFormats.V4L2_PIX_FMT_H264_NO_SC ||
                fmtDesc.PixelFormat == V4L2PixelFormats.V4L2_PIX_FMT_H264_SLICE)
            {
                _logger.LogTrace(
                    "Found H264 format: {Format} ({Description})",
                    fmtDesc.PixelFormat,
                    fmtDesc.DescriptionString);
                return true;
            }

            fmtDesc.Index++;
        }

        return false;
    }

    private V4l2H264DecoderType DetermineDecoderType(int fd)
    {
        // Stateless decoders use H264_SLICE format and have stateless control IDs
        // Check for H264_SLICE support in output formats
        bool hasSliceFormat = false;
        bool hasStatefulFormat = false;

        var fmtDesc = new V4L2FmtDesc
        {
            Index = 0,
            Type = V4L2BufferType.VIDEO_OUTPUT_MPLANE
        };

        while (LibV4L2.EnumerateFormat(fd, ref fmtDesc).Success)
        {
            if (fmtDesc.PixelFormat == V4L2PixelFormats.V4L2_PIX_FMT_H264_SLICE)
            {
                hasSliceFormat = true;
                _logger.LogTrace("Device supports H264_SLICE format (stateless indicator)");
            }
            else if (fmtDesc.PixelFormat == V4L2PixelFormats.V4L2_PIX_FMT_H264 ||
                     fmtDesc.PixelFormat == V4L2PixelFormats.V4L2_PIX_FMT_H264_NO_SC)
            {
                hasStatefulFormat = true;
                _logger.LogTrace("Device supports H264 format (stateful indicator)");
            }

            fmtDesc.Index++;
        }

        // Check for stateless controls as additional confirmation
        bool hasStatelessControls = CheckStatelessControls(fd);

        if (hasSliceFormat && hasStatelessControls)
        {
            return V4l2H264DecoderType.Stateless;
        }

        if (hasStatefulFormat)
        {
            return V4l2H264DecoderType.Stateful;
        }

        // If has slice format but not controls, still consider it stateless
        if (hasSliceFormat)
        {
            _logger.LogDebug("Device has H264_SLICE format but missing some stateless controls, treating as stateless");
            return V4l2H264DecoderType.Stateless;
        }

        return V4l2H264DecoderType.None;
    }

    private bool CheckStatelessControls(int fd)
    {
        // Try to query stateless H264 decode mode control
        var qext = new V4L2QueryExtCtrl
        {
            Id = V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SPS
        };

        var result = LibV4L2.QueryExtendedControl(fd, ref qext);
        if (result.Success)
        {
            _logger.LogTrace("Device has V4L2_CID_STATELESS_H264_SPS control");
            return true;
        }

        // Also check for decode mode control
        qext.Id = V4l2ControlsConstants.V4L2_CID_STATELESS_H264_DECODE_MODE;
        result = LibV4L2.QueryExtendedControl(fd, ref qext);
        if (result.Success)
        {
            _logger.LogTrace("Device has V4L2_CID_STATELESS_H264_DECODE_MODE control");
            return true;
        }

        return false;
    }

    private string? TryFindMediaDevice(string busInfo)
    {
        // Media devices are at /dev/media*
        // We need to find the one that matches our video device's bus info
        if (!Directory.Exists("/dev"))
        {
            return null;
        }

        var mediaDevices = Directory.GetFiles("/dev", "media*");

        foreach (var mediaPath in mediaDevices)
        {
            // For now, use a simple heuristic - match the first media device
            // A more robust implementation would use the media controller API
            // to verify the association
            if (File.Exists(mediaPath))
            {
                _logger.LogTrace("Found potential media device: {Path}", mediaPath);
                return mediaPath;
            }
        }

        return null;
    }
}
