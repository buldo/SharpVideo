using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Decoding;
using SharpVideo.Decoding.V4l2;
using SharpVideo.Decoding.V4l2.Discovery;
using SharpVideo.Decoding.V4l2.Stateful;
using SharpVideo.Decoding.V4l2.Stateless;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Utils;
using SharpVideo.V4L2;

namespace SharpVideo.V4L2DecodeDrmPreviewDemo2;

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

        drmDevice.EnableDrmCapabilities(Logger);

        if (!DmaBuffersAllocator.TryCreate(out var allocator) || allocator == null)
        {
            throw new Exception("Failed to create DMA buffers allocator.");
        }

        var drmBufferManagerLogger = LoggerFactory.CreateLogger<DrmBufferManager>();
        using var drmBufferManager = new DrmBufferManager(
            drmDevice,
            allocator,
            [KnownPixelFormats.DRM_FORMAT_NV12, KnownPixelFormats.DRM_FORMAT_ARGB8888],
            drmBufferManagerLogger);
        var presenter = DrmPresenter.Create(
            drmDevice,
            Width,
            Height,
            drmBufferManager,
            KnownPixelFormats.DRM_FORMAT_ARGB8888,  // Primary plane format (with alpha for transparency)
            KnownPixelFormats.DRM_FORMAT_NV12,      // Overlay plane format (video)
            Logger);

        // Configure z-order: Primary plane (transparent) on top, Overlay plane (video) below
        // Note: Z-position may not be supported on all hardware (e.g., some Raspberry Pi configurations)
        Logger.LogInformation("Configuring plane z-order: video below, transparent primary on top");
        var primaryZposRange = presenter.PrimaryPlane.GetPlaneZPositionRange();
        var overlayZposRange = presenter.OverlayPlane.GetPlaneZPositionRange();

        if (primaryZposRange.HasValue && overlayZposRange.HasValue)
        {
            var primarySuccess = presenter.PrimaryPlane.SetPlaneZPosition(primaryZposRange.Value.max);
            var overlaySuccess = presenter.OverlayPlane.SetPlaneZPosition(overlayZposRange.Value.min);

            if (primarySuccess && overlaySuccess)
            {
                Logger.LogInformation("Set Primary zpos={PrimaryZ} (top), Overlay zpos={OverlayZ} (bottom)",
                    primaryZposRange.Value.max, overlayZposRange.Value.min);
            }
            else
            {
                Logger.LogWarning("Failed to set z-position: Primary={PrimarySuccess}, Overlay={OverlaySuccess}. " +
                    "Using default layer ordering.", primarySuccess, overlaySuccess);
            }
        }
        else
        {
            Logger.LogWarning("Z-position not supported on this hardware. Primary zpos available: {PrimaryAvail}, " +
                "Overlay zpos available: {OverlayAvail}. Using default layer ordering.",
                primaryZposRange.HasValue, overlayZposRange.HasValue);
        }

        var decoder = CreateV4l2Decoder(drmBufferManager);

        var player = new Player(presenter, (BaseDecoder<SharedDmaBuffer>)decoder, LoggerFactory);
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

        presenter.Dispose();

    }

    /// <summary>
    /// Creates a V4L2 hardware decoder with automatic hardware detection.
    /// </summary>
    /// <param name="drmBufferManager">DRM buffer manager for zero-copy decoding.</param>
    /// <returns>A V4L2 decoder (stateless or stateful based on hardware).</returns>
    private static IDecoder CreateV4l2Decoder(DrmBufferManager drmBufferManager)
    {
        var provider = new V4l2H264DecoderProvider(LoggerFactory.CreateLogger<V4l2H264DecoderProvider>());
        var decoderInfo = provider.FindBestDecoder();
        if (decoderInfo == null)
        {
            throw new Exception("Failed to find V4L2 H264 decoder");
        }

        Logger.LogInformation(
            "Found {DecoderType} decoder at {Path} ({Driver}: {Card})",
            decoderInfo.DecoderType,
            decoderInfo.DevicePath,
            decoderInfo.Driver,
            decoderInfo.Card);

        var device = V4L2DeviceFactory.Open(decoderInfo.DevicePath);
        if (device == null)
        {
            throw new Exception($"Failed to open device {decoderInfo.DevicePath}");
        }

        IDecoder? decoder;

        if (decoderInfo.DecoderType == V4l2H264DecoderType.Stateful)
        {
            decoder = V4l2H264StatefulDecoder.Create(
                device,
                LoggerFactory,
                null,
                drmBufferManager);
        }
        else if (decoderInfo.DecoderType == V4l2H264DecoderType.Stateless)
        {
            var mediaDevice = MediaDevice.Open(decoderInfo.MediaDevicePath!);
            if (mediaDevice == null)
            {
                throw new Exception($"Failed to open media device {decoderInfo.MediaDevicePath}");
            }

            decoder = V4l2H264StatelessDecoder.Create(
                device,
                mediaDevice,
                LoggerFactory,
                null,
                drmBufferManager);
        }
        else
        {
            throw new Exception($"Unknown decoder type: {decoderInfo.DecoderType}");
        }

        decoder.Initialize();
        return decoder;
    }

    private static FileStream GetFileStream()
    {
        var testVideoName = "ohd_video.h264";
        var filePath = File.Exists(testVideoName) ? testVideoName : Path.Combine(AppContext.BaseDirectory, testVideoName);
        if (!File.Exists(filePath))
        {
            throw new Exception(
                $"Error: Test video file '{testVideoName}' not found in current directory or application base directory.");
        }

        return File.OpenRead(filePath);
    }

}