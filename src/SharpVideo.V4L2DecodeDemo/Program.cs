using Microsoft.Extensions.Logging;
using SharpVideo.V4L2DecodeDemo.Services;

namespace SharpVideo.V4L2DecodeDemo;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("SharpVideo H.264 V4L2 Decoder Demo");
        Console.WriteLine("===================================\n");

        try
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
                builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            var logger = loggerFactory.CreateLogger<H264V4L2StreamingDecoder>();

            var decoder = new H264V4L2StreamingDecoder(logger);

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

            await decoder.DecodeFileAsync("test_video.h264");
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