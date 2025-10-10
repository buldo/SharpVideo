using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.V4L2;
using SharpVideo.V4L2StatelessDecoder.Models;
using SharpVideo.V4L2StatelessDecoder.Services;

namespace SharpVideo.V4L2DecodeDrmPreviewDemo;

[SupportedOSPlatform("linux")]
internal class Program
{
    static async Task Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
                builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<Program>();

        logger.LogInformation("SharpVideo H.264 V4L2 Decoder Demo");

        await using var fileStream = GetFileStream();
        using var v4L2Device = GetVideoDevice(logger);
        using var mediaDevice = GetMediaDevice();
        var drmDevice = GetDrmDevice();
        var dmaBuffersAllocator = GetDmaBuffersAllocator();

        var decoderLogger = loggerFactory.CreateLogger<H264V4L2StatelessDecoder>();
        var config = new DecoderConfiguration
        {
            OutputBufferCount = 16,
            CaptureBufferCount = 16,
            RequestPoolSize = 32
        };

        int decodedFrames = 0;
        await using var decoder = new H264V4L2StatelessDecoder(v4L2Device, mediaDevice, decoderLogger, config, span =>
        {
            decodedFrames++;
            //saver.TryEnqueueFrame(span, 1920, 1080);
        });

        var decodeStopWatch = Stopwatch.StartNew();
        await decoder.DecodeStreamAsync(fileStream);

        logger.LogInformation("Decoding completed successfully in {ElapsedTime:F2} seconds!", decodeStopWatch.Elapsed.TotalSeconds);
        logger.LogInformation("Amount of decoded frames: {DecodedFrames}", decodedFrames);
    }

    private static V4L2Device GetVideoDevice(ILogger logger)
    {
        var h264Devices = V4L2.V4L2DeviceManager.GetH264Devices();
        if (!h264Devices.Any())
        {
            throw new Exception("Error: No H.264 capable V4L2 devices found.");
        }

        var selectedDevice = h264Devices.First();
        logger.LogInformation("Using device: {@Device}", selectedDevice);

        var v4L2Device = V4L2DeviceFactory.Open(selectedDevice.DevicePath);
        if (v4L2Device == null)
        {
            throw new Exception($"Error: Failed to open V4L2 device at path '{selectedDevice.DevicePath}'.");
        }

        return v4L2Device;
    }

    private static MediaDevice GetMediaDevice()
    {
        // TODO: media device discovery
        var mediaDevice = MediaDevice.Open("/dev/media0");
        if (mediaDevice == null)
        {
            throw new Exception("Not able to open /dev/media0");
        }

        return mediaDevice;
    }

    private static FileStream GetFileStream()
    {
        var testVideoName = "test_video.h264";
        var filePath = File.Exists(testVideoName) ? testVideoName : Path.Combine(AppContext.BaseDirectory, testVideoName);
        if (!File.Exists(filePath))
        {
            throw new Exception(
                $"Error: Test video file '{testVideoName}' not found in current directory or application base directory.");
        }

        return File.OpenRead(filePath);
    }

    private static DrmDevice GetDrmDevice()
    {
        var devices = Directory.EnumerateFiles("/dev/dri", "card*", SearchOption.TopDirectoryOnly);
        DrmDevice? drmDevice = null;
        foreach (var device in devices)
        {
            drmDevice = DrmDevice.Open(device);
            if (drmDevice != null)
            {
                Console.WriteLine($"Opened DRM device: {device}");
                break;
            }
        }

        if (drmDevice == null)
        {
            throw new Exception("Drm device not found");
        }

        return drmDevice;
    }

    private static DmaBuffersAllocator GetDmaBuffersAllocator()
    {
        if (!DmaBuffersAllocator.TryCreate(out var allocator) || allocator == null)
        {
            throw new Exception("Failed to create dma buffer");
        }

        return allocator;
    }
}