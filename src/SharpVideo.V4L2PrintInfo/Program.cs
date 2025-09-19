using System;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2PrintInfo
{
    [SupportedOSPlatform("linux")]
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Searching for V4L2 M2M (codec) devices...");
            var allEntries = Directory.GetFileSystemEntries("/dev/", "video*");

            if (allEntries.Length == 0)
            {
                Console.WriteLine("No V4L2 devices found in /dev/");
                return;
            }

            // Filter out symlinks that point to files in the same directory
            var devices = FilterSymlinks(allEntries);

            // Sort devices for consistent output
            Array.Sort(devices);

            int m2mDevicesFound = 0;
            foreach (var devicePath in devices)
            {
                if (PrintM2mDeviceInfo(devicePath))
                {
                    m2mDevicesFound++;
                }
            }

            if (m2mDevicesFound == 0)
            {
                Console.WriteLine("\nNo V4L2 M2M devices found in system.");
            }
        }

        private static string[] FilterSymlinks(string[] allEntries)
        {
            var result = new List<string>();
            var devDirectory = "/dev/";

            foreach (var entry in allEntries)
            {
                var fileInfo = new FileInfo(entry);

                // If it's a regular file, add it
                if (!fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    result.Add(entry);
                }
                else
                {
                    // It's a symlink, check if target is in the same directory
                    try
                    {
                        var linkTarget = fileInfo.ResolveLinkTarget(false);
                        if (linkTarget != null)
                        {
                            var targetPath = linkTarget.FullName;
                            var targetDirectory = Path.GetDirectoryName(targetPath);

                            // If the target is not in the same directory, include the symlink
                            if (!string.Equals(targetDirectory, devDirectory.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                            {
                                result.Add(entry);
                            }
                            // If target is in same directory, skip the symlink (we'll process the original)
                        }
                        else
                        {
                            // If we can't resolve the target, include it anyway
                            result.Add(entry);
                        }
                    }
                    catch
                    {
                        // If there's any error resolving the symlink, include it anyway
                        result.Add(entry);
                    }
                }
            }

            return result.ToArray();
        }

        private static bool PrintM2mDeviceInfo(string devicePath)
        {
            int fd = -1;
            try
            {
                fd = Libc.open(devicePath, OpenFlags.O_RDWR);
                if (fd < 0) return false;

                var queryResult = LibV4L2.QueryCapabilities(fd, out var capability);
                if (!queryResult.Success) return false;

                bool isM2mDevice = ((uint)capability.Capabilities & ((uint)V4L2Capabilities.VIDEO_M2M_MPLANE | (uint)V4L2Capabilities.VIDEO_M2M)) != 0;

                if (!isM2mDevice) return false;

                Console.WriteLine($"\n--- Found V4L2 M2M Device: {devicePath} ---");
                Console.WriteLine($"  Driver: {capability.DriverString}");
                Console.WriteLine($"  Card: {capability.CardString}");
                Console.WriteLine($"  Bus Info: {capability.BusInfoString}");
                Console.WriteLine($"  Capabilities: 0x{(uint)capability.Capabilities:X8}");
                Console.WriteLine($"    {FormatCapabilities((V4L2Capabilities)capability.Capabilities)}");

                PrintFormatsSection(fd, V4L2BufferType.VIDEO_OUTPUT, "OUTPUT formats (single-planar)");
                PrintFormatsSection(fd, V4L2BufferType.VIDEO_OUTPUT_MPLANE, "OUTPUT formats (multi-planar)");
                PrintFormatsSection(fd, V4L2BufferType.VIDEO_CAPTURE, "CAPTURE formats (single-planar)");
                PrintFormatsSection(fd, V4L2BufferType.VIDEO_CAPTURE_MPLANE, "CAPTURE formats (multi-planar)");

                Console.WriteLine("--- End of Info ---");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error querying device {devicePath}: {ex.Message}");
                return false;
            }
            finally
            {
                if (fd >= 0) Libc.close(fd);
            }
        }

        private static void PrintFormatsSection(int fd, V4L2BufferType bufferType, string sectionTitle)
        {
            var formats = new List<string>();

            for (uint i = 0; ; i++)
            {
                var fmtDesc = new V4L2FmtDesc
                {
                    Index = i,
                    Type = (uint)bufferType
                };

                var result = LibV4L2.EnumerateFormat(fd, ref fmtDesc);
                if (!result.Success) break;

                var flagsStr = new StringBuilder();
                if ((fmtDesc.Flags & V4L2FormatFlags.COMPRESSED) != 0) flagsStr.Append("[Compressed] ");
                if ((fmtDesc.Flags & V4L2FormatFlags.EMULATED) != 0) flagsStr.Append("[Emulated] ");

                var fourccString = FourCC.ToString(fmtDesc.PixelFormat);
                formats.Add($"    - {fmtDesc.DescriptionString} ({fourccString}) {flagsStr.ToString().Trim()}");
            }

            // Only print the section title if there are formats to display
            if (formats.Count > 0)
            {
                Console.WriteLine($"\n  {sectionTitle}:");
                foreach (var format in formats)
                {
                    Console.WriteLine(format);
                }
            }
        }

        private static string FormatCapabilities(V4L2Capabilities capabilities)
        {
            var capList = new List<string>();

            if (capabilities.HasFlag(V4L2Capabilities.VIDEO_CAPTURE))
                capList.Add("VIDEO_CAPTURE");
            if (capabilities.HasFlag(V4L2Capabilities.VIDEO_OUTPUT))
                capList.Add("VIDEO_OUTPUT");
            if (capabilities.HasFlag(V4L2Capabilities.VIDEO_OVERLAY))
                capList.Add("VIDEO_OVERLAY");
            if (capabilities.HasFlag(V4L2Capabilities.VBI_CAPTURE))
                capList.Add("VBI_CAPTURE");
            if (capabilities.HasFlag(V4L2Capabilities.VBI_OUTPUT))
                capList.Add("VBI_OUTPUT");
            if (capabilities.HasFlag(V4L2Capabilities.SLICED_VBI_CAPTURE))
                capList.Add("SLICED_VBI_CAPTURE");
            if (capabilities.HasFlag(V4L2Capabilities.SLICED_VBI_OUTPUT))
                capList.Add("SLICED_VBI_OUTPUT");
            if (capabilities.HasFlag(V4L2Capabilities.RDS_CAPTURE))
                capList.Add("RDS_CAPTURE");
            if (capabilities.HasFlag(V4L2Capabilities.VIDEO_OUTPUT_OVERLAY))
                capList.Add("VIDEO_OUTPUT_OVERLAY");
            if (capabilities.HasFlag(V4L2Capabilities.HW_FREQ_SEEK))
                capList.Add("HW_FREQ_SEEK");
            if (capabilities.HasFlag(V4L2Capabilities.RDS_OUTPUT))
                capList.Add("RDS_OUTPUT");
            if (capabilities.HasFlag(V4L2Capabilities.VIDEO_CAPTURE_MPLANE))
                capList.Add("VIDEO_CAPTURE_MPLANE");
            if (capabilities.HasFlag(V4L2Capabilities.VIDEO_OUTPUT_MPLANE))
                capList.Add("VIDEO_OUTPUT_MPLANE");
            if (capabilities.HasFlag(V4L2Capabilities.VIDEO_M2M_MPLANE))
                capList.Add("VIDEO_M2M_MPLANE");
            if (capabilities.HasFlag(V4L2Capabilities.VIDEO_M2M))
                capList.Add("VIDEO_M2M");
            if (capabilities.HasFlag(V4L2Capabilities.TUNER))
                capList.Add("TUNER");
            if (capabilities.HasFlag(V4L2Capabilities.AUDIO))
                capList.Add("AUDIO");
            if (capabilities.HasFlag(V4L2Capabilities.RADIO))
                capList.Add("RADIO");
            if (capabilities.HasFlag(V4L2Capabilities.MODULATOR))
                capList.Add("MODULATOR");
            if (capabilities.HasFlag(V4L2Capabilities.SDR_CAPTURE))
                capList.Add("SDR_CAPTURE");
            if (capabilities.HasFlag(V4L2Capabilities.EXT_PIX_FORMAT))
                capList.Add("EXT_PIX_FORMAT");
            if (capabilities.HasFlag(V4L2Capabilities.SDR_OUTPUT))
                capList.Add("SDR_OUTPUT");
            if (capabilities.HasFlag(V4L2Capabilities.META_CAPTURE))
                capList.Add("META_CAPTURE");
            if (capabilities.HasFlag(V4L2Capabilities.READWRITE))
                capList.Add("READWRITE");
            if (capabilities.HasFlag(V4L2Capabilities.ASYNCIO))
                capList.Add("ASYNCIO");
            if (capabilities.HasFlag(V4L2Capabilities.STREAMING))
                capList.Add("STREAMING");
            if (capabilities.HasFlag(V4L2Capabilities.META_OUTPUT))
                capList.Add("META_OUTPUT");
            if (capabilities.HasFlag(V4L2Capabilities.TOUCH))
                capList.Add("TOUCH");
            if (capabilities.HasFlag(V4L2Capabilities.IO_MC))
                capList.Add("IO_MC");
            if (capabilities.HasFlag(V4L2Capabilities.DEVICE_CAPS))
                capList.Add("DEVICE_CAPS");

            return string.Join(", ", capList);
        }
    }
}
