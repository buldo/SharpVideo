using System.Runtime.Versioning;
using System.Text;

using Microsoft.Extensions.Logging;

using SharpVideo.Decoding;
using SharpVideo.Decoding.V4l2;
using SharpVideo.Decoding.V4l2.Discovery;
using SharpVideo.Decoding.V4l2.Stateful;
using SharpVideo.Decoding.V4l2.Stateless;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;
using SharpVideo.Utils;
using SharpVideo.V4L2;

namespace SharpVideo.V4L2DecodeDrmPreviewDemo2;

/// <summary>
/// Demonstrates H.264 video decoding using V4L2 stateless decoder with zero-copy DRM display.
/// Uses DMABUF sharing between V4L2 decoder and DRM display for efficient video presentation.
/// Now uses DualPlanePresenter2 with OUT_FENCE_PTR for precise buffer synchronization.
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
        Logger.LogInformation("SharpVideo H.264 V4L2 Decoder with DRM Preview Demo (DualPlanePresenter2)");

        // Setup DRM display
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
            [KnownPixelFormats.DRM_FORMAT_NV12],
            drmBufferManagerLogger);

        // Get device resources to find connector, CRTC, and mode
        var resources = drmDevice.GetResources();
        if (resources == null)
        {
            throw new Exception("Failed to get DRM resources");
        }

        // Find the first connected connector
        var connector = resources.Connectors
            .FirstOrDefault(c => c.Connection == DrmModeConnection.Connected);

        if (connector == null)
        {
            throw new Exception("No connected display found");
        }

        Logger.LogInformation("Found connector: {Type}-{TypeId} (ID: {Id})",
            connector.ConnectorType, connector.ConnectorTypeId, connector.ConnectorId);

        // Get the preferred mode (or first mode matching Width x Height)
        var mode = connector.Modes
            .FirstOrDefault(m => m.HDisplay == Width && m.VDisplay == Height)
            ?? connector.Modes.FirstOrDefault();

        if (mode == null)
        {
            throw new Exception("No suitable display mode found");
        }

        Logger.LogInformation("Using mode: {Name} ({Width}x{Height}@{Hz}Hz)",
            mode.Name, mode.HDisplay, mode.VDisplay, mode.VRefresh);

        // Get CRTC ID from the connector's current encoder
        uint crtcId = connector.Encoder?.CrtcId ?? 0;
        if (crtcId == 0)
        {
            // Fall back to first CRTC
            crtcId = resources.Crtcs.FirstOrDefault();
            if (crtcId == 0)
            {
                throw new Exception("No CRTC found");
            }
        }

        Logger.LogInformation("Using CRTC: {CrtcId}", crtcId);

        // Find a plane supporting NV12 for video-only mode
        var crtcList = resources.Crtcs.ToList();
        var crtcIndex = crtcList.IndexOf(crtcId);
        var crtcMask = 1u << crtcIndex;

        var videoPlane = resources.Planes
            .Where(p => (p.PossibleCrtcs & crtcMask) != 0)
            .FirstOrDefault(p => p.Formats.Contains(KnownPixelFormats.DRM_FORMAT_NV12.Fourcc));

        if (videoPlane == null)
        {
            throw new Exception($"No plane found supporting NV12 format for CRTC {crtcId}");
        }

        Logger.LogInformation("Selected video plane: {PlaneId}", videoPlane.Id);

        // Build the presenter configuration (video-only mode)
        var presenterConfig = DualPlanePresenterConfig.CreateBuilder()
            .WithVideoPlane(videoPlane, new PlaneDrawConfiguration((uint)Width, (uint)Height))
            .WithCrtc(crtcId)
            .WithConnector(connector.ConnectorId)
            .WithMode(ConvertToNativeMode(mode))
            .WithLogger(Logger)
            .Build();

        // Create the dual-plane presenter
        using var presenter = new DualPlanePresenter2(drmDevice, presenterConfig);

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

    /// <summary>
    /// Converts a managed DrmModeInfo to native DrmModeModeInfo.
    /// </summary>
    private static DrmModeModeInfo ConvertToNativeMode(DrmModeInfo mode)
    {
        var nativeMode = new DrmModeModeInfo
        {
            Clock = mode.Clock,
            HDisplay = mode.HDisplay,
            HSyncStart = mode.HSyncStart,
            HSyncEnd = mode.HSyncEnd,
            HTotal = mode.HTotal,
            HSkew = mode.HSkew,
            VDisplay = mode.VDisplay,
            VSyncStart = mode.VSyncStart,
            VSyncEnd = mode.VSyncEnd,
            VTotal = mode.VTotal,
            VScan = mode.VScan,
            VRefresh = mode.VRefresh,
            Flags = mode.Flags,
            Type = mode.Type
        };

        unsafe
        {
            var nameBytes = Encoding.UTF8.GetBytes(mode.Name);
            for (int i = 0; i < Math.Min(nameBytes.Length, 32); i++)
            {
                nativeMode.Name[i] = nameBytes[i];
            }
        }

        return nativeMode;
    }
}