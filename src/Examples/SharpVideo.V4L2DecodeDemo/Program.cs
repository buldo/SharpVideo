using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.V4L2;
using SharpVideo.V4L2Decoding.Models;
using SharpVideo.V4L2Decoding.Services;
using SixLabors.ImageSharp;

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
      if (!File.Exists(filePath))
        {
            throw new Exception(
        $"Error: Test video file '{testVideoName}' not found in current directory or application base directory.");
        }

        var h264Devices = V4L2.V4L2DeviceManager.GetH264Devices();
        if (!h264Devices.Any())
        {
   throw new Exception("Error: No H.264 capable V4L2 devices found.");
        }

        var selectedDevice = h264Devices.First();
        logger.LogInformation("Using device: {@Device}", selectedDevice);

 using var v4L2Device = V4L2DeviceFactory.Open(selectedDevice.DevicePath);
        if(v4L2Device == null)
        {
            throw new Exception($"Error: Failed to open V4L2 device at path '{selectedDevice.DevicePath}'.");    }

     // Detect decoder type
     var decoderType = H264V4L2DecoderFactory.DetectDecoderType(selectedDevice);
        logger.LogInformation("Detected decoder type: {DecoderType}", decoderType);

        // Open media device only if using stateless decoder
        MediaDevice? mediaDevice = null;
        if (decoderType == DecoderType.Stateless)
        {
            // TODO: media device discovery
   mediaDevice = MediaDevice.Open("/dev/media0");
 if (mediaDevice == null)
            {
       logger.LogWarning("Failed to open /dev/media0 - stateless decoder may not work properly");
     }
}

        using var _ = mediaDevice; // Ensure disposal

        var decoderLogger = loggerFactory.CreateLogger("H264Decoder");
        var config = new DecoderConfiguration
        {
  OutputBufferCount = 16,
            CaptureBufferCount = 16,
    RequestPoolSize = 32 // Only used for stateless
   };

        var saver = new FrameSaver("frames", loggerFactory.CreateLogger<FrameSaver>());

        int decodedFrames = 0;
        
        // Create appropriate decoder using factory
        await using var decoder = H264V4L2DecoderFactory.CreateDecoder(
         decoderType,
     v4L2Device,
    mediaDevice,
    decoderLogger,
        config,
        span =>
     {
 decodedFrames++;
     //saver.TryEnqueueFrame(span, 1920, 1080);
      },
   null);

    await using var fileStream = File.OpenRead(filePath);
        var decodeStopWatch = Stopwatch.StartNew();
    decoder.InitializeDecoder(null);
        await decoder.DecodeStreamAsync(fileStream);

        logger.LogInformation("Decoding completed successfully in {ElapsedTime:F2} seconds!", decodeStopWatch.Elapsed.TotalSeconds);
logger.LogInformation("Amount of decoded frames: {DecodedFrames}", decodedFrames);
    }
}