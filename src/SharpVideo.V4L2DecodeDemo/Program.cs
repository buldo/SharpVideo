using System;
using System.Threading.Tasks;
using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2DecodeDemo
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("SharpVideo H.264 V4L2 Decoder Demo");
            Console.WriteLine("===================================\n");

            try
            {
                var decoder = new H264V4L2Decoder();
                await decoder.DecodeFileAsync("test_video.h264");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }
    }

    internal class H264V4L2Decoder
    {
        public Task DecodeFileAsync(string h264FilePath)
        {
            return Task.Run(() => DecodeFile(h264FilePath));
        }

        private void DecodeFile(string h264FilePath)
        {
            Console.WriteLine($"Decoding H.264 file: {h264FilePath}");

            // Find H.264 decoder device
            var devicePath = FindH264DecoderDevice();
            if (string.IsNullOrEmpty(devicePath))
            {
                throw new InvalidOperationException("No H.264 decoder device found");
            }

            Console.WriteLine($"Found decoder device: {devicePath}");
            Console.WriteLine("Note: Full implementation requires unsafe code and detailed V4L2 setup");
            Console.WriteLine("This is a basic implementation demonstrating device discovery");
        }

        private string? FindH264DecoderDevice()
        {
            Console.WriteLine("Searching for H.264 capable V4L2 devices...");

            for (int i = 0; i < 64; i++)
            {
                var devicePath = $"/dev/video{i}";

                if (!File.Exists(devicePath))
                    continue;

                var fd = Libc.open(devicePath, OpenFlags.O_RDWR);
                if (fd < 0)
                    continue;

                try
                {
                    // Query device capabilities
                    var result = LibV4L2.QueryCapabilities(fd, out var caps);
                    if (!result.Success)
                        continue;

                    var driverName = caps.DriverString;
                    var cardName = caps.CardString;

                    Console.WriteLine($"Device {devicePath}: {driverName} - {cardName}");

                    // Check if it's a video decoder (M2M device)
                    var deviceCaps = (V4L2Capabilities)caps.DeviceCaps;
                    if (!deviceCaps.HasFlag(V4L2Capabilities.VIDEO_M2M_MPLANE) &&
                        !deviceCaps.HasFlag(V4L2Capabilities.VIDEO_M2M))
                    {
                        Console.WriteLine($"  -> Not a memory-to-memory device");
                        continue;
                    }

                    // Check for H.264 support by enumerating output formats
                    bool supportsH264 = false;
                    Console.WriteLine($"  -> Checking supported formats...");
                    for (uint fmtIndex = 0; fmtIndex < 32; fmtIndex++)
                    {
                        var fmtDesc = new V4L2FmtDesc
                        {
                            Index = fmtIndex,
                            Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE
                        };

                        var fmtResult = LibV4L2.EnumerateFormat(fd, ref fmtDesc);
                        if (!fmtResult.Success)
                            break;

                        Console.WriteLine($"     Format {fmtIndex}: {fmtDesc.DescriptionString} (FOURCC: 0x{fmtDesc.PixelFormat:X8})");

                        // Check for various H.264 format variants
                        if (fmtDesc.PixelFormat == V4L2PixelFormats.H264 ||
                            fmtDesc.PixelFormat == 0x34363253) // H264 Parsed Slice Data 'S264'
                        {
                            supportsH264 = true;
                            Console.WriteLine($"  -> Supports H.264 decoding (FOURCC: 0x{fmtDesc.PixelFormat:X8})");
                            break;
                        }
                    }

                    if (supportsH264)
                    {
                        return devicePath;
                    }
                }
                finally
                {
                    Libc.close(fd);
                }
            }

            return null;
        }
    }
}
