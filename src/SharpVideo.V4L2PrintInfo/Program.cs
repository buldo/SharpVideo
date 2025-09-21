using System.Runtime.Versioning;
using System.Text;
using SharpVideo.Linux.Native;
using SharpVideo.V4L2;

namespace SharpVideo.V4L2PrintInfo
{
    [SupportedOSPlatform("linux")]
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Searching for V4L2 devices...");
            var deviceInfos = V4L2DeviceManager.GetVideoDevices();

            if (deviceInfos.Length == 0)
            {
                Console.WriteLine("No V4L2 devices found in /dev/");
                return;
            }

            // Sort devices for consistent output
            Array.Sort(deviceInfos);

            foreach (var devicePath in deviceInfos)
            {
                using var device = V4L2DeviceFactory.Open(devicePath);
                if (device != null)
                {
                    PrintM2mDeviceInfo(device);
                }
            }
        }

        private static bool PrintM2mDeviceInfo(V4L2Device device)
        {
            try
            {

                var queryResult = LibV4L2.QueryCapabilities(device.fd, out var capability);
                if (!queryResult.Success) return false;

                bool isM2mDevice = ((uint)capability.Capabilities & ((uint)V4L2Capabilities.VIDEO_M2M_MPLANE | (uint)V4L2Capabilities.VIDEO_M2M)) != 0;

                if (!isM2mDevice) return false;

                Console.WriteLine($"\n--- Found V4L2 M2M Device: {device} ---");
                Console.WriteLine($"  Driver: {capability.DriverString}");
                Console.WriteLine($"  Card: {capability.CardString}");
                Console.WriteLine($"  Bus Info: {capability.BusInfoString}");
                Console.WriteLine($"  Capabilities: 0x{(uint)capability.Capabilities:X8}");
                Console.WriteLine($"    {FormatCapabilities((V4L2Capabilities)capability.Capabilities)}");

                PrintFormatsSection(device.fd, V4L2BufferType.VIDEO_OUTPUT, "OUTPUT formats (single-planar)");
                PrintFormatsSection(device.fd, V4L2BufferType.VIDEO_OUTPUT_MPLANE, "OUTPUT formats (multi-planar)");
                PrintFormatsSection(device.fd, V4L2BufferType.VIDEO_CAPTURE, "CAPTURE formats (single-planar)");
                PrintFormatsSection(device.fd, V4L2BufferType.VIDEO_CAPTURE_MPLANE, "CAPTURE formats (multi-planar)");

                PrintControls(device.fd);

                Console.WriteLine("--- End of Info ---");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error querying device {device}: {ex.Message}");
                return false;
            }
        }

        private static unsafe void PrintControls(int fd)
        {
            Console.WriteLine("\n  Available Controls:");

            var queryCtrl = new V4L2QueryCtrl { Id = (uint)V4L2Constants.V4L2_CTRL_FLAG_NEXT_CTRL };

            while (LibV4L2.QueryControl(fd, ref queryCtrl).Success)
            {
                if ((queryCtrl.Flags & V4L2ControlFlags.DISABLED) != 0)
                {
                    queryCtrl.Id |= (uint)V4L2Constants.V4L2_CTRL_FLAG_NEXT_CTRL;
                    continue;
                }

                Console.WriteLine($"    - {queryCtrl.Name} (ID: 0x{queryCtrl.Id:X8})");
                Console.WriteLine($"      Type: {queryCtrl.Type}, Flags: {queryCtrl.Flags}");
                Console.WriteLine($"      Min: {queryCtrl.Minimum}, Max: {queryCtrl.Maximum}, Step: {queryCtrl.Step}, Default: {queryCtrl.DefaultValue}");

                if (queryCtrl.Type == V4L2CtrlType.Menu || queryCtrl.Type == V4L2CtrlType.IntegerMenu)
                {
                    Console.WriteLine("      Menu Items:");
                    var queryMenuItem = new V4L2QueryMenuItem();
                    for (uint i = (uint)queryCtrl.Minimum; i <= queryCtrl.Maximum; i++)
                    {
                        queryMenuItem.Id = queryCtrl.Id;
                        queryMenuItem.Index = i;
                        if (LibV4L2.QueryMenuItem(fd, ref queryMenuItem).Success)
                        {
                            if (queryCtrl.Type == V4L2CtrlType.Menu)
                            {
                                Console.WriteLine($"        {queryMenuItem.Index}: {queryMenuItem.Name}");
                            }
                            else // IntegerMenu
                            {
                                Console.WriteLine($"        {queryMenuItem.Index}: {queryMenuItem.Value}");
                            }
                        }
                    }
                }

                queryCtrl.Id |= (uint)V4L2Constants.V4L2_CTRL_FLAG_NEXT_CTRL;
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
