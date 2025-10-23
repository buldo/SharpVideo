using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;
using SharpVideo.Utils;
using SharpVideo.V4L2;
using SharpVideo.V4L2Decoding.Models;
using SharpVideo.V4L2Decoding.Services;

namespace SharpVideo.V4L2DecodeDrmPreviewDemo;

/// <summary>
/// Demonstrates H.264 video decoding using V4L2 stateless decoder with zero-copy DRM display.
/// Uses DMABUF sharing between V4L2 decoder and DRM display for efficient video presentation.
/// </summary>
[SupportedOSPlatform("linux")]
internal class Program
{
    private const int Width = 1920;
    private const int Height = 1080;

    private static readonly ILoggerFactory LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory
        .Create(builder => builder.AddConsole()
        #if DEBUG
        .SetMinimumLevel(LogLevel.Trace)
        #else
        .SetMinimumLevel(LogLevel.Warning)
        #endif
        );

    private static readonly ILogger Logger = LoggerFactory.CreateLogger<Program>();

    static async Task Main(string[] args)
    {
        Logger.LogInformation("SharpVideo H.264 V4L2 Decoder with DRM Preview Demo");

        // Setup DRM display
        // Note: DrmDevice should implement IDisposable in the future for proper resource management
        var drmDevice = DrmUtils.OpenDrmDevice(Logger);
        if (drmDevice == null)
        {
            throw new Exception("No DRM devices could be opened");
        }

        EnableDrmCapabilities(drmDevice, Logger);

        if (!DmaBuffersAllocator.TryCreate(out var allocator) || allocator == null)
        {
            throw new Exception("Failed to create DMA buffers allocator.");
        }

        var drmBufferManagerLogger = LoggerFactory.CreateLogger<DrmBufferManager>();
        using var drmBufferManager = new DrmBufferManager(
            drmDevice,
            allocator,
            [KnownPixelFormats.DRM_FORMAT_NV12, KnownPixelFormats.DRM_FORMAT_XRGB8888],
            drmBufferManagerLogger);
        var presenter = DrmPresenter.Create(drmDevice, Width, Height, drmBufferManager, Logger);

        var (v4L2Device, deviceInfo) = GetVideoDevice(Logger);
        using var _ = v4L2Device; // Ensure disposal

        var config = new DecoderConfiguration
        {
            // Use more buffers if streaming is supported for smoother playback
            OutputBufferCount = 3u,
            CaptureBufferCount = 6u,
            RequestPoolSize = 6,
            UseDrmPrimeBuffers = true // Enable zero-copy DMABUF mode for lowest latency
        };

        var decoderLogger = LoggerFactory.CreateLogger<H264V4L2StatelessDecoder>();
        using var mediaDevice = GetMediaDevice();
        await using var decoder = new H264V4L2StatelessDecoder(
            v4L2Device,
            mediaDevice,
            decoderLogger,
            config,
            processDecodedAction: null, // Not used in DMABUF mode
            drmBufferManager: drmBufferManager);

        var playerLogger = LoggerFactory.CreateLogger<Player>();
        var player = new Player(presenter, decoder, playerLogger);
        player.Init();

        await using var fileStream = GetFileStream();
        player.StartPlay(fileStream);
        player.WaitCompleted();

        await Task.Delay(100);

        Logger.LogWarning("=== Final Statistics===");
        Logger.LogWarning("Decoding stream completed in {ElapsedTime:F2} seconds", player.Statistics.DecodeElapsed.TotalSeconds);
        Logger.LogWarning("Decoded {FrameCount} frames, average decode FPS: {Fps:F2}", player.Statistics.DecodedFrames, player.Statistics.DecodedFrames / player.Statistics.DecodeElapsed.TotalSeconds);
        Logger.LogWarning("Displayed {FrameCount} frames, average present FPS: {Fps:F2}", player.Statistics.PresentedFrames, player.Statistics.PresentedFrames / player.Statistics.PresentElapsed.TotalSeconds);
        Logger.LogWarning("Processing completed successfully!");

        presenter.CleanupDisplay();

    }

    private static (V4L2Device device, V4L2DeviceInfo deviceInfo) GetVideoDevice(ILogger logger)
    {
        var h264Devices = V4L2.V4L2DeviceManager.GetH264Devices();
        if (!h264Devices.Any())
        {
            throw new Exception("Error: No H.264 capable V4L2 devices found.");
        }

        var selectedDevice = h264Devices.First();
        logger.LogInformation("Using device: {@Device}", selectedDevice);

        // Log device capabilities for optimization analysis
        LogDeviceCapabilities(selectedDevice, logger);

        var v4L2Device = V4L2DeviceFactory.Open(selectedDevice.DevicePath);
        if (v4L2Device == null)
        {
            throw new Exception($"Error: Failed to open V4L2 device at path '{selectedDevice.DevicePath}'.");
        }

        return (v4L2Device, selectedDevice);
    }

    private static void LogDeviceCapabilities(V4L2DeviceInfo deviceInfo, ILogger logger)
    {
        logger.LogInformation("Driver: {Driver}; Card: {Card}; Device Path: {Path}; DeviceCapabilities: {DeviceCapabilities}", deviceInfo.DriverName, deviceInfo.CardName, deviceInfo.DevicePath, deviceInfo.DeviceCapabilities);

        logger.LogInformation("=== Supported Formats ===");
        foreach (var format in deviceInfo.SupportedFormats)
        {
            logger.LogInformation("  Format: {Description} (FourCC: {FourCC})",
                format.Description, format.PixelFormat);
        }
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

    private static List<DrmClientCapability> EnableDrmCapabilities(DrmDevice drmDevice, ILogger logger)
    {
        var capsToEnable = new[]
        {
            DrmClientCapability.DRM_CLIENT_CAP_UNIVERSAL_PLANES,
            DrmClientCapability.DRM_CLIENT_CAP_ATOMIC
        };

        logger.LogInformation("Enabling DRM client capabilities");
        List<DrmClientCapability> enabledCaps = new();
        foreach (var cap in capsToEnable)
        {
            if (drmDevice.TrySetClientCapability(cap, true, out var code))
            {
                logger.LogInformation("Enabled {Capability}", cap);
                enabledCaps.Add(cap);
            }
            else
            {
                logger.LogWarning("Failed to enable {Capability}: error {Code}", cap, code);
            }
        }

        return enabledCaps;
    }
}