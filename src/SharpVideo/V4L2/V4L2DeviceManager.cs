using System.Runtime.Versioning;

using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

[SupportedOSPlatform("linux")]
public static class V4L2DeviceManager
{
    public static string[] GetVideoDevices()
    {
        return Directory.EnumerateFileSystemEntries("/dev", "video*")
            .Where(CheckForSymlink)
            .ToArray();

        bool CheckForSymlink(string path)
        {
            var fileInfo = new FileInfo(path);

            // This is not symlink
            if (!fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            var target = fileInfo.ResolveLinkTarget(true);
            if (target == null)
            {
                return true;
            }

            if(target.FullName.StartsWith("/dev/video"))
            {
                return false;
            }

            return true;
        }
    }

    public static V4L2DeviceInfo[] GetH264Devices()
    {
        var devices = FindByOutputPixelFormat(
        [
            V4L2PixelFormats.V4L2_PIX_FMT_H264, V4L2PixelFormats.V4L2_PIX_FMT_H264_MVC,
            V4L2PixelFormats.V4L2_PIX_FMT_H264_NO_SC, V4L2PixelFormats.V4L2_PIX_FMT_H264_SLICE
        ]);
        return devices.ToArray();
    }

    public static List<V4L2DeviceInfo> FindByOutputPixelFormat(HashSet<uint> formats)
    {
        var allDevices = GetVideoDevices();

        var matchingDevices = new List<V4L2DeviceInfo>();
        foreach (var device in allDevices)
        {
            var deviceInfo = GetDeviceInfo(device);
            if (deviceInfo == null)
            {
                continue;
            }

            if (deviceInfo.SupportedFormats.Any(format => formats.Contains(format.PixelFormat)))
            {
                matchingDevices.Add(deviceInfo);
            }
        }

        return matchingDevices;
    }

    private static V4L2DeviceInfo? GetDeviceInfo(string devicePath)
    {

        var fd = Libc.open(devicePath, OpenFlags.O_RDWR);
        if (fd < 0)
        {
            return null;
        }

        try
        {
            // Query device capabilities
            var result = LibV4L2.QueryCapabilities(fd, out var caps);
            if (!result.Success)
            {
                return null;
            }

            var driverName = caps.DriverString;
            var cardName = caps.CardString;
            var deviceCaps = caps.DeviceCaps;

            // Check if it's a memory-to-memory device
            bool isM2M = deviceCaps.HasFlag(V4L2Capabilities.VIDEO_M2M_MPLANE) ||
                         deviceCaps.HasFlag(V4L2Capabilities.VIDEO_M2M);

            // Enumerate supported formats
            var formats = GetSupportedFormats(fd);

            return new V4L2DeviceInfo
            {
                DevicePath = devicePath,
                DriverName = driverName,
                CardName = cardName,
                DeviceCapabilities = caps.DeviceCaps,
                SupportedFormats = formats,
                IsMemoryToMemoryDevice = isM2M
            };
        }
        finally
        {
            Libc.close(fd);
        }

    }

    private static List<V4L2FormatInfo> GetSupportedFormats(int fd)
    {
        var formats = new List<V4L2FormatInfo>();

        for (uint fmtIndex = 0; fmtIndex < 32; fmtIndex++)
        {
            var fmtDesc = new V4L2FmtDesc
            {
                Index = fmtIndex,
                Type = V4L2BufferType.VIDEO_OUTPUT_MPLANE
            };

            var result = LibV4L2.EnumerateFormat(fd, ref fmtDesc);
            if (!result.Success)
                break;

            var formatInfo = new V4L2FormatInfo
            {
                Index = fmtIndex,
                PixelFormat = fmtDesc.PixelFormat,
                Description = fmtDesc.DescriptionString,
                BufferType =  fmtDesc.Type
            };

            formats.Add(formatInfo);
        }

        return formats;
    }
}