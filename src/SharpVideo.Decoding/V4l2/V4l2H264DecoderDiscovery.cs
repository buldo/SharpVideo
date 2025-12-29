using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.C;
using SharpVideo.Linux.Native.V4L2;
using SharpVideo.V4L2;

namespace SharpVideo.Decoding.V4l2;

/// <summary>
/// Describes the type of V4L2 H264 decoder detected.
/// </summary>
public enum V4l2H264DecoderType
{
    /// <summary>
    /// No V4L2 H264 decoder found.
    /// </summary>
    None,

    /// <summary>
    /// Stateless H264 decoder (requires userspace to manage decoding state).
    /// Examples: Raspberry Pi, Rockchip RK3588.
    /// </summary>
    Stateless,

    /// <summary>
    /// Stateful H264 decoder (decoder manages state internally).
    /// Examples: Qualcomm Venus, some other SoCs.
    /// </summary>
    Stateful
}

/// <summary>
/// Represents a discovered V4L2 H264 decoder device.
/// </summary>
public sealed class V4l2H264DecoderInfo
{
    /// <summary>
    /// The device path (e.g., /dev/video10).
    /// </summary>
    public required string DevicePath { get; init; }

    /// <summary>
    /// The type of decoder.
    /// </summary>
    public required V4l2H264DecoderType DecoderType { get; init; }

    /// <summary>
    /// The driver name.
    /// </summary>
    public required string Driver { get; init; }

    /// <summary>
    /// The card/device name.
    /// </summary>
    public required string Card { get; init; }

    /// <summary>
    /// Path to the associated media device (for stateless decoders), if found.
    /// </summary>
    public string? MediaDevicePath { get; init; }
}

/// <summary>
/// Service for discovering V4L2 H264 hardware decoders.
/// </summary>
[SupportedOSPlatform("linux")]
public static class V4l2H264DecoderDiscovery
{
    /// <summary>
    /// Discovers all available V4L2 H264 decoders on the system.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    /// <returns>List of discovered decoder information.</returns>
    public static IReadOnlyList<V4l2H264DecoderInfo> DiscoverDecoders(ILogger? logger = null)
    {
        var results = new List<V4l2H264DecoderInfo>();

        // Scan /dev/video* devices
        if (!Directory.Exists("/dev"))
        {
            logger?.LogDebug("No /dev directory found, skipping V4L2 discovery");
            return results;
        }

        var videoDevices = Directory.GetFiles("/dev", "video*")
            .OrderBy(p => p)
            .ToList();

        logger?.LogDebug("Found {Count} video devices to scan", videoDevices.Count);

        foreach (var devicePath in videoDevices)
        {
            var info = TryProbeDevice(devicePath, logger);
            if (info != null)
            {
                results.Add(info);
                logger?.LogInformation(
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
    /// <param name="logger">Optional logger for diagnostic output.</param>
    /// <returns>The best decoder info, or null if no hardware decoder found.</returns>
    public static V4l2H264DecoderInfo? FindBestDecoder(ILogger? logger = null)
    {
        var decoders = DiscoverDecoders(logger);

        // Prefer stateless decoders (more control, lower latency potential)
        var stateless = decoders.FirstOrDefault(d => d.DecoderType == V4l2H264DecoderType.Stateless);
        if (stateless != null)
        {
            return stateless;
        }

        // Fall back to stateful
        return decoders.FirstOrDefault(d => d.DecoderType == V4l2H264DecoderType.Stateful);
    }

    private static V4l2H264DecoderInfo? TryProbeDevice(string devicePath, ILogger? logger)
    {
        int fd = Libc.open(devicePath, OpenFlags.O_RDWR | OpenFlags.O_NONBLOCK);
        if (fd < 0)
        {
            logger?.LogTrace("Cannot open {Path}", devicePath);
            return null;
        }

        try
        {
            // Query capabilities
            var capResult = LibV4L2.QueryCapabilities(fd, out var capability);
            if (!capResult.Success)
            {
                logger?.LogTrace("Cannot query capabilities for {Path}", devicePath);
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
                logger?.LogTrace("{Path} is not a M2M device", devicePath);
                return null;
            }

            // Check if it supports H264 input format
            if (!SupportsH264Input(fd, logger))
            {
                logger?.LogTrace("{Path} does not support H264 input", devicePath);
                return null;
            }

            // Determine if stateless or stateful
            var decoderType = DetermineDecoderType(fd, logger);
            if (decoderType == V4l2H264DecoderType.None)
            {
                logger?.LogTrace("{Path} is not an H264 decoder", devicePath);
                return null;
            }

            // Try to find associated media device for stateless decoders
            string? mediaDevicePath = null;
            if (decoderType == V4l2H264DecoderType.Stateless)
            {
                mediaDevicePath = TryFindMediaDevice(capability.BusInfoString, logger);
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

    private static bool SupportsH264Input(int fd, ILogger? logger)
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
                logger?.LogTrace(
                    "Found H264 format: {Format} ({Description})",
                    fmtDesc.PixelFormat,
                    fmtDesc.DescriptionString);
                return true;
            }

            fmtDesc.Index++;
        }

        return false;
    }

    private static V4l2H264DecoderType DetermineDecoderType(int fd, ILogger? logger)
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
                logger?.LogTrace("Device supports H264_SLICE format (stateless indicator)");
            }
            else if (fmtDesc.PixelFormat == V4L2PixelFormats.V4L2_PIX_FMT_H264 ||
                     fmtDesc.PixelFormat == V4L2PixelFormats.V4L2_PIX_FMT_H264_NO_SC)
            {
                hasStatefulFormat = true;
                logger?.LogTrace("Device supports H264 format (stateful indicator)");
            }

            fmtDesc.Index++;
        }

        // Check for stateless controls as additional confirmation
        bool hasStatelessControls = CheckStatelessControls(fd, logger);

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
            logger?.LogDebug("Device has H264_SLICE format but missing some stateless controls, treating as stateless");
            return V4l2H264DecoderType.Stateless;
        }

        return V4l2H264DecoderType.None;
    }

    private static bool CheckStatelessControls(int fd, ILogger? logger)
    {
        // Try to query stateless H264 decode mode control
        var qext = new V4L2QueryExtCtrl
        {
            Id = V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SPS
        };

        var result = LibV4L2.QueryExtendedControl(fd, ref qext);
        if (result.Success)
        {
            logger?.LogTrace("Device has V4L2_CID_STATELESS_H264_SPS control");
            return true;
        }

        // Also check for decode mode control
        qext.Id = V4l2ControlsConstants.V4L2_CID_STATELESS_H264_DECODE_MODE;
        result = LibV4L2.QueryExtendedControl(fd, ref qext);
        if (result.Success)
        {
            logger?.LogTrace("Device has V4L2_CID_STATELESS_H264_DECODE_MODE control");
            return true;
        }

        return false;
    }

    private static string? TryFindMediaDevice(string busInfo, ILogger? logger)
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
                logger?.LogTrace("Found potential media device: {Path}", mediaPath);
                return mediaPath;
            }
        }

        return null;
    }
}
