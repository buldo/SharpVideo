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
        using var loggerFactory = LoggerFactory.Create(builder =>
                builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<Program>();

        logger.LogInformation("SharpVideo H.264 V4L2 Decoder Demo");

        var testVideoName = "test_video.h264";
        var filePath = File.Exists(testVideoName) ? testVideoName : Path.Combine(AppContext.BaseDirectory, testVideoName);
        if (!File.Exists(testVideoName))
        {
            logger.LogError($"Error: Test video file '{testVideoName}' not found in current directory or application base directory.");
            return;
        }


        var h264Devices = V4L2.V4L2DeviceManager.GetH264Devices();
        if (!h264Devices.Any())
        {
            logger.LogError("Error: No H.264 capable V4L2 devices found.");
            return;
        }

        var selectedDevice = h264Devices.First();
        logger.LogInformation("Using device: {@Device}", selectedDevice);

        var device = V4L2.V4L2DeviceFactory.Open(selectedDevice.DevicePath);
        if(device == null)
        {
            logger.LogError($"Error: Failed to open V4L2 device at path '{selectedDevice.DevicePath}'.");
            return;
        }

        logger.LogInformation("Successfully opened V4L2 device");

        var decoderLogger = loggerFactory.CreateLogger<H264V4L2StatelessDecoder>();
        await using var decoder = new H264V4L2StatelessDecoder(device, decoderLogger);

        // Subscribe to events for real-time feedback
        var lastProgressUpdate = DateTime.MinValue;
        decoder.FrameDecoded += (sender, e) =>
        {
            if (e.FrameNumber % 30 == 0 || e.FrameNumber <= 10) // Log first 10 frames and every 30th frame
            {
                logger.LogInformation($"Frame {e.FrameNumber} decoded: {e.BytesUsed} bytes at {e.Timestamp:HH:mm:ss.fff}");
            }
        };

        decoder.ProgressChanged += (sender, e) =>
        {
            // Throttle progress updates to avoid spam
            var now = DateTime.Now;
            if (now - lastProgressUpdate > TimeSpan.FromSeconds(1) || e.ProgressPercentage >= 100)
            {
                logger.LogInformation($"Progress: {e.ProgressPercentage:F1}% ({e.FramesDecoded} frames, {e.FramesPerSecond:F2} fps)");
                lastProgressUpdate = now;
            }
        };

        // Use NALU-by-NALU decoding for better hardware compatibility
        logger.LogInformation("Starting NALU-by-NALU decoding for optimal hardware decoder compatibility...");
        logger.LogInformation("This may take some time depending on file size and hardware capabilities.\n");

        var startTime = DateTime.Now;
        using var fileStream = File.OpenRead(filePath);
        await decoder.DecodeStreamAsync(fileStream);
        var elapsed = DateTime.Now - startTime;

        logger.LogInformation("Decoding completed successfully in {ElapsedTime:F2} seconds!", elapsed.TotalSeconds);
        logger.LogInformation("All frames have been processed by the hardware decoder.");
    }
}