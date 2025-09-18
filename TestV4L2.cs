using System;
using SharpVideo.Linux.Native;

namespace TestV4L2
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Testing V4L2 functionality...");

            // Test V4L2 constants
            Console.WriteLine($"VIDIOC_QUERYCAP: 0x{V4L2Constants.VIDIOC_QUERYCAP:X8}");
            Console.WriteLine($"VIDIOC_S_FMT: 0x{V4L2Constants.VIDIOC_S_FMT:X8}");
            Console.WriteLine($"VIDIOC_STREAMON: 0x{V4L2Constants.VIDIOC_STREAMON:X8}");

            // Test V4L2 structures
            var capability = new V4L2Capability();
            Console.WriteLine($"V4L2Capability size: {System.Runtime.InteropServices.Marshal.SizeOf<V4L2Capability>()} bytes");

            var format = new V4L2Format();
            Console.WriteLine($"V4L2Format size: {System.Runtime.InteropServices.Marshal.SizeOf<V4L2Format>()} bytes");

            var buffer = new V4L2Buffer();
            Console.WriteLine($"V4L2Buffer size: {System.Runtime.InteropServices.Marshal.SizeOf<V4L2Buffer>()} bytes");

            var requestBuffers = new V4L2RequestBuffers();
            Console.WriteLine($"V4L2RequestBuffers size: {System.Runtime.InteropServices.Marshal.SizeOf<V4L2RequestBuffers>()} bytes");

            var exportBuffer = new V4L2ExportBuffer();
            Console.WriteLine($"V4L2ExportBuffer size: {System.Runtime.InteropServices.Marshal.SizeOf<V4L2ExportBuffer>()} bytes");

            var decoderCmd = new V4L2DecoderCmd();
            Console.WriteLine($"V4L2DecoderCmd size: {System.Runtime.InteropServices.Marshal.SizeOf<V4L2DecoderCmd>()} bytes");

            // Test helper methods
            try
            {
                int fd = Libc.open("/dev/null", OpenFlags.O_RDWR);
                if (fd >= 0)
                {
                    try
                    {
                        var result = LibV4L2.QueryCapabilities(fd, out var cap);
                        Console.WriteLine($"Query capabilities result: Success={result.Success}, Error={result.ErrorCode}");

                        var (formatResult, fmt) = LibV4L2.SetMultiplanarCaptureFormat(fd, 1920, 1080, V4L2PixelFormats.NV12M, 2);
                        Console.WriteLine($"Set format result: Success={formatResult.Success}, Error={formatResult.ErrorCode}");
                        Console.WriteLine($"Format: {fmt.Pix_mp.Width}x{fmt.Pix_mp.Height}, planes={fmt.Pix_mp.NumPlanes}");
                    }
                    finally
                    {
                        Libc.close(fd);
                    }
                }
                Console.WriteLine("V4L2 functionality test completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during testing: {ex.Message}");
            }
        }
    }
}