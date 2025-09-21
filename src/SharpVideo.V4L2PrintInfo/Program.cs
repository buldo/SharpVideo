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

                Console.WriteLine($"\n--- Found V4L2 M2M Device: {device} ---");
                Console.WriteLine($"  Driver: {capability.DriverString}");
                Console.WriteLine($"  Card: {capability.CardString}");
                Console.WriteLine($"  Bus Info: {capability.BusInfoString}");
                Console.WriteLine($"  Capabilities: 0x{(uint)capability.Capabilities:X8}");
                Console.WriteLine($"    {capability.Capabilities}");

                PrintFormatsSection(device.fd, V4L2BufferType.VIDEO_OUTPUT, "OUTPUT formats (single-planar)");
                PrintFormatsSection(device.fd, V4L2BufferType.VIDEO_OUTPUT_MPLANE, "OUTPUT formats (multi-planar)");
                PrintFormatsSection(device.fd, V4L2BufferType.VIDEO_CAPTURE, "CAPTURE formats (single-planar)");
                PrintFormatsSection(device.fd, V4L2BufferType.VIDEO_CAPTURE_MPLANE, "CAPTURE formats (multi-planar)");

                PrintControls(device);

                Console.WriteLine("--- End of Info ---");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error querying device {device}: {ex.Message}");
                return false;
            }
        }

        private static unsafe void PrintControls(V4L2Device device)
        {
            Console.WriteLine("\n  Available Controls:");

            foreach (var control in device.Controls)
            {
                Console.WriteLine($"    - {control.Name} (ID: 0x{control.Id:X8})");
                Console.WriteLine($"      Type: {control.Type}, Flags: {control.Flags}");
                Console.WriteLine($"      Min: {control.Minimum}, Max: {control.Maximum}, Step: {control.Step}, Default: {control.DefaultValue}");
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
    }
}
