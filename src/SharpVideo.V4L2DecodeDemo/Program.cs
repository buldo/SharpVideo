using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.V4L2DecodeDemo.Services;

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
                }
                else
                {
                    Console.WriteLine($"Error: Could not find H.264 file. Checked: {filePath} and {fallback}");
                    Environment.Exit(1);
                    return;
                }
            }

            using var loggerFactory = LoggerFactory.Create(builder =>
                builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            var logger = loggerFactory.CreateLogger<H264V4L2StreamingDecoder>();

            await using var decoder = new H264V4L2StreamingDecoder(logger);

            // Subscribe to events for real-time feedback
            decoder.FrameDecoded += (sender, e) =>
            {
                if (e.FrameNumber % 10 == 0) // Log every 10th frame to avoid spam
                {
                    Console.WriteLine($"Frame {e.FrameNumber} decoded: {e.BytesUsed} bytes at {e.Timestamp:HH:mm:ss.fff}");
                }
            };

            decoder.ProgressChanged += (sender, e) =>
            {
                if (e.FramesDecoded % 30 == 0) // Update progress every 30 frames
                {
                    Console.WriteLine($"Progress: {e.ProgressPercentage:F1}% ({e.FramesDecoded} frames, {e.FramesPerSecond:F2} fps)");
                }
            };

            // Use NALU-by-NALU decoding for better hardware compatibility
            Console.WriteLine("Using NALU-by-NALU decoding for optimal hardware decoder compatibility...");
            await decoder.DecodeFileNaluByNaluAsync(filePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
            Environment.Exit(1);
        }
    }
}