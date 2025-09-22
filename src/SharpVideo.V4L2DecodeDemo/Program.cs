using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.V4L2DecodeDemo.Services;
using SharpVideo.V4L2DecodeDemo.Services.Stateless;
using SharpVideo.V4L2DecodeDemo.Interfaces;

namespace SharpVideo.V4L2DecodeDemo;

[SupportedOSPlatform("linux")]
internal class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("SharpVideo H.264 V4L2 Decoder Demo");
        Console.WriteLine("===================================\n");

        try
        {
            // Determine input file path: allow CLI arg override, otherwise use test file next to the executable
            var filePath = args.Length > 0
                ? args[0]
                : Path.Combine(AppContext.BaseDirectory, "test_video.h264");

            Console.WriteLine($"Input file: {filePath}");
            if (!File.Exists(filePath))
            {
                // Fallback: try current working directory for convenience when running from project root
                var fallback = Path.GetFullPath("test_video.h264");
                if (File.Exists(fallback))
                {
                    filePath = fallback;
                    Console.WriteLine($"Using fallback file: {filePath}");
                }
                else
                {
                    Console.WriteLine($"Error: Could not find H.264 file. Checked: {filePath} and {fallback}");
                    Console.WriteLine("Please provide a valid H.264 file as a command line argument or place 'test_video.h264' in the executable directory.");
                    Environment.Exit(1);
                    return;
                }
            }

            using var loggerFactory = LoggerFactory.Create(builder =>
                builder.AddConsole().SetMinimumLevel(LogLevel.Information)); // Changed to Information to reduce spam

            // Find and validate H.264 capable devices
            var h264Devices = V4L2.V4L2DeviceManager.GetH264Devices();
            if (!h264Devices.Any())
            {
                Console.WriteLine("Error: No H.264 capable V4L2 devices found.");
                Console.WriteLine("Make sure your system has hardware video decoders available and accessible.");
                Console.WriteLine("You may need to run as root or add your user to the 'video' group.");
                Environment.Exit(1);
                return;
            }

            Console.WriteLine($"Found {h264Devices.Count()} H.264 capable device(s):");
            foreach (var deviceInfo in h264Devices)
            {
                Console.WriteLine($"  - {deviceInfo.DevicePath}");
            }

            var selectedDevice = h264Devices.First();
            Console.WriteLine($"Using device: {selectedDevice.DevicePath}");

            // Open device with error handling
            V4L2.V4L2Device? device = null;
            try
            {
                device = V4L2.V4L2DeviceFactory.Open(selectedDevice.DevicePath);
                Console.WriteLine("Successfully opened V4L2 device");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: Failed to open V4L2 device {selectedDevice.DevicePath}: {ex.Message}");
                Console.WriteLine("This may indicate insufficient permissions or device is in use.");
                Environment.Exit(1);
                return;
            }

            var logger = loggerFactory.CreateLogger<H264V4L2StatelessDecoder>();

            try
            {
                await using var decoder = new H264V4L2StatelessDecoder(device, logger);

                // Subscribe to events for real-time feedback
                var lastProgressUpdate = DateTime.MinValue;
                decoder.FrameDecoded += (sender, e) =>
                {
                    if (e.FrameNumber % 30 == 0 || e.FrameNumber <= 10) // Log first 10 frames and every 30th frame
                    {
                        Console.WriteLine($"Frame {e.FrameNumber} decoded: {e.BytesUsed} bytes at {e.Timestamp:HH:mm:ss.fff}");
                    }
                };

                decoder.ProgressChanged += (sender, e) =>
                {
                    // Throttle progress updates to avoid spam
                    var now = DateTime.Now;
                    if (now - lastProgressUpdate > TimeSpan.FromSeconds(1) || e.ProgressPercentage >= 100)
                    {
                        Console.WriteLine($"Progress: {e.ProgressPercentage:F1}% ({e.FramesDecoded} frames, {e.FramesPerSecond:F2} fps)");
                        lastProgressUpdate = now;
                    }
                };

                // Use NALU-by-NALU decoding for better hardware compatibility
                Console.WriteLine("Starting NALU-by-NALU decoding for optimal hardware decoder compatibility...");
                Console.WriteLine("This may take some time depending on file size and hardware capabilities.\n");

                var startTime = DateTime.Now;
                await decoder.DecodeFileNaluByNaluAsync(filePath);
                var elapsed = DateTime.Now - startTime;

                Console.WriteLine($"\nDecoding completed successfully in {elapsed.TotalSeconds:F2} seconds!");
                Console.WriteLine("All frames have been processed by the hardware decoder.");
            }
            catch (FileNotFoundException ex)
            {
                Console.WriteLine($"Error: Video file not found: {ex.Message}");
                Environment.Exit(1);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Error: Decoder operation failed: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Details: {ex.InnerException.Message}");
                }
                Console.WriteLine("\nThis could be due to:");
                Console.WriteLine("- Unsupported H.264 profile/level in the input file");
                Console.WriteLine("- Hardware decoder limitations");
                Console.WriteLine("- Insufficient system resources");
                Console.WriteLine("- Corrupted input file");
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: Unexpected failure during decoding: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                Console.WriteLine($"\nStack trace:\n{ex.StackTrace}");
                Environment.Exit(1);
            }
            finally
            {
                device?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
            Console.WriteLine("\nThis indicates a critical system or configuration issue.");
            Environment.Exit(1);
        }
    }
}