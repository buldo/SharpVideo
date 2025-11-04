using System.Diagnostics;
using System.Net;
using System.Runtime.Versioning;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Logging;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.C;
using SharpVideo.Utils;
using SharpVideo.V4L2;
using SharpVideo.V4L2Decoding.Models;
using SharpVideo.V4L2Decoding.Services;
using SharpVideo.ImGui;

namespace SharpVideo.RtpPlayerDemo;

/// <summary>
/// RTP H.264 Player with V4L2 hardware decoding, DRM display, and ImGui OSD
/// Receives RTP stream on UDP 0.0.0.0:5600 and displays video with statistics overlay
/// </summary>
[SupportedOSPlatform("linux")]
internal class Program
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const string BindAddress = "0.0.0.0";
    private const int BindPort = 5600;

    private static readonly ILoggerFactory LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory
        .Create(builder => builder.AddConsole()
#if DEBUG
            .SetMinimumLevel(LogLevel.Debug)
#else
            .SetMinimumLevel(LogLevel.Information)
#endif
        );

    private static readonly ILogger Logger = LoggerFactory.CreateLogger<Program>();

    static async Task Main(string[] args)
    {
        Logger.LogInformation("=== SharpVideo RTP H.264 Player ===");
        Logger.LogInformation("Listening on {Address}:{Port}", BindAddress, BindPort);
        Logger.LogInformation("Press ESC or Ctrl+C to exit");

        // Set environment for DRM
        Environment.SetEnvironmentVariable("EGL_PLATFORM", "drm");

        // Setup graceful shutdown
        using var shutdownHandler = new ShutdownHandler(Logger);

        try
        {
            await RunPlayerAsync(shutdownHandler.Token);
        }
        catch (OperationCanceledException)
        {
            Logger.LogInformation("Application cancelled gracefully");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Fatal error in RTP player");
            throw;
        }

        Logger.LogInformation("RTP Player exited successfully");
    }

    private static async Task RunPlayerAsync(CancellationToken cancellationToken)
    {
        // Setup DRM display
        Logger.LogDebug("Opening DRM device...");
        var drmDevice = DrmUtils.OpenDrmDevice(Logger);
        if (drmDevice == null)
        {
            throw new Exception("No DRM devices could be opened");
        }
        drmDevice.EnableDrmCapabilities(Logger);

        // Create GBM device for ImGui rendering
        var gbmDevice = GbmDevice.CreateFromDrmDevice(drmDevice);
        Logger.LogInformation("Created GBM device for ImGui rendering");

        // Create DMA buffer allocator for video
        if (!DmaBuffersAllocator.TryCreate(out var allocator) || allocator == null)
        {
            throw new Exception("Failed to create DMA buffers allocator");
        }

        var drmBufferManagerLogger = LoggerFactory.CreateLogger<DrmBufferManager>();
        using var drmBufferManager = new DrmBufferManager(
            drmDevice,
            allocator,
            [KnownPixelFormats.DRM_FORMAT_NV12, KnownPixelFormats.DRM_FORMAT_ARGB8888],
            drmBufferManagerLogger);

        // Create unified DRM presenter with GBM atomic primary (ImGui) and DMA overlay (video)
        var presenter = DrmPresenter.CreateWithGbmAtomicAndDmaOverlay(
            drmDevice,
            Width,
            Height,
            gbmDevice,
            drmBufferManager,
            KnownPixelFormats.DRM_FORMAT_ARGB8888,  // Primary plane for ImGui
            KnownPixelFormats.DRM_FORMAT_NV12,      // Overlay plane for video
            Logger);

        if (presenter == null)
        {
            throw new Exception("Failed to create unified DRM presenter");
        }

        // Setup input manager
        Logger.LogInformation("Initializing input system...");
        using var inputManager = new InputManager((uint)Width, (uint)Height,
            LoggerFactory.CreateLogger<InputManager>());

        // Configure ImGui - get GBM surface from atomic presenter
        var gbmAtomicPresenter = presenter.AsGbmAtomicPresenter();
        if (gbmAtomicPresenter == null)
        {
            throw new Exception("Failed to get GBM atomic presenter");
        }

        var imguiConfig = new ImGuiDrmConfiguration
        {
            Width = (uint)Width,
            Height = (uint)Height,
            DrmDevice = drmDevice,
            GbmDevice = gbmDevice,
            GbmSurfaceHandle = gbmAtomicPresenter.GetNativeGbmSurfaceHandle(),
            PixelFormat = KnownPixelFormats.DRM_FORMAT_ARGB8888,
            ConfigFlags = ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.DockingEnable,
            DrawCursor = true,
            UiScale = 1.0f,
            GlslVersion = "#version 300 es",
            EnableInput = true
        };

        // Create ImGui manager
        using var imguiManager = new ImGuiManager(
            imguiConfig,
            inputManager,
            LoggerFactory.CreateLogger<ImGuiManager>());

        // Setup V4L2 decoder
        var (v4L2Device, deviceInfo) = GetVideoDevice(Logger);
        using var _ = v4L2Device;

        var decoderConfig = new DecoderConfiguration
        {
            OutputBufferCount = 3u,
            CaptureBufferCount = 6u,
            RequestPoolSize = 6,
            UseDrmPrimeBuffers = true
        };

        var decoderLogger = LoggerFactory.CreateLogger<H264V4L2StatelessDecoder>();
        using var mediaDevice = GetMediaDevice();
        await using var decoder = new H264V4L2StatelessDecoder(
            v4L2Device,
            mediaDevice,
            decoderLogger,
            decoderConfig,
            processDecodedAction: null,
            drmBufferManager: drmBufferManager);

        // Setup RTP receiver
        using var rtpReceiver = new RtpReceiverService(
            new IPEndPoint(IPAddress.Parse(BindAddress), BindPort),
            LoggerFactory);

        // Create decoder pipeline - presenter now works directly with overlay
        var pipelineLogger = LoggerFactory.CreateLogger<DecoderPipeline>();
        await using var pipeline = new DecoderPipeline(
            rtpReceiver,
            decoder,
            presenter,
            pipelineLogger);

        pipeline.Initialize();

        // Create OSD renderer
        var osdRenderer = new OsdRenderer(pipeline.Statistics, rtpReceiver);

        // Start RTP receiver and pipeline
        rtpReceiver.Start();
        pipeline.Start();

        Logger.LogInformation("RTP receiver started on {Address}:{Port}", BindAddress, BindPort);

        // Warmup ImGui frame
        Logger.LogInformation("Rendering initial warmup frame...");
        if (imguiManager.WarmupFrame(dt => osdRenderer.Render()))
        {
            if (gbmAtomicPresenter.SubmitFrame())
            {
                Logger.LogInformation("Warmup frame submitted successfully");
            }
        }

        Thread.Sleep(100);

        // Main loop
        await RunMainLoopAsync(imguiManager, gbmAtomicPresenter, inputManager, osdRenderer, pipeline.Statistics, cancellationToken);

        // Cleanup
        await pipeline.StopAsync();
        presenter.Dispose();
        gbmDevice.Dispose();
        drmDevice.Dispose();

        // Print final statistics
        Logger.LogInformation("=== Final Statistics ===");
        Logger.LogInformation("RTP Received: {Count} frames", rtpReceiver.ReceivedFramesCount);
        Logger.LogInformation("RTP Dropped: {Count} frames", rtpReceiver.DroppedFramesCount);
        Logger.LogInformation("Decoded: {Count} frames @ {Fps:F2} FPS", 
            pipeline.Statistics.DecodedFrames, pipeline.Statistics.AverageDecodeFps);
        Logger.LogInformation("Presented: {Count} frames @ {Fps:F2} FPS", 
            pipeline.Statistics.PresentedFrames, pipeline.Statistics.AveragePresentFps);
        Logger.LogInformation("Avg decode time: {Time:F2} ms/frame", 
            pipeline.Statistics.AverageDecodeTimeMs);
    }

    private static async Task RunMainLoopAsync(
        ImGuiManager imguiManager,
        DrmPlaneGbmAtomicPresenter primaryPresenter,
        InputManager inputManager,
        OsdRenderer osdRenderer,
        PlayerStatistics statistics,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("=== Main Loop Started ===");

        var stopwatch = Stopwatch.StartNew();
        var frameCount = 0;
        var droppedFrames = 0;
        var lastFpsTime = stopwatch.Elapsed;
        var exiting = false;

        var inputFd = inputManager.GetFileDescriptor();

        while (!exiting)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var currentTime = stopwatch.Elapsed;

                // Poll input events (non-blocking)
                var pollFd = new PollFd
                {
                    fd = inputFd,
                    events = PollEvents.POLLIN
                };

                var pollResult = Libc.poll(ref pollFd, 1, 0);
                if (pollResult > 0)
                {
                    inputManager.ProcessEvents();
                }

                // Check for ESC key to exit
                if (inputManager.IsKeyDown(1)) // KEY_ESC = 1
                {
                    Logger.LogInformation("ESC key pressed, exiting");
                    exiting = true;
                    break;
                }

                // Process OSD input
                osdRenderer.ProcessInput(inputManager);

                // Update statistics FPS counters
                statistics.UpdateFps();

                // Render ImGui OSD frame
                var frameRendered = imguiManager.RenderFrame(dt => osdRenderer.Render());

                if (frameRendered)
                {
                    if (primaryPresenter.SubmitFrame())
                    {
                        frameCount++;
                    }
                    else
                    {
                        droppedFrames++;
                    }
                }

                // Log FPS periodically
                if ((currentTime - lastFpsTime).TotalSeconds >= 5.0)
                {
                    var totalFrames = frameCount + droppedFrames;
                    var renderFps = totalFrames / (currentTime - lastFpsTime).TotalSeconds;

                    Logger.LogInformation(
                        "ImGui Render FPS: {RenderFps:F1} | OSD Frames: {Count} | Dropped: {Dropped}",
                        renderFps, frameCount, droppedFrames);

                    frameCount = 0;
                    droppedFrames = 0;
                    lastFpsTime = currentTime;
                }

                // Small delay to prevent CPU spinning
                await Task.Delay(1, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Logger.LogInformation("Main loop cancelled");
                exiting = true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Exception in main loop");
                exiting = true;
            }
        }

        Logger.LogInformation("Main loop exited");
    }

    private static (V4L2Device device, V4L2DeviceInfo deviceInfo) GetVideoDevice(ILogger logger)
    {
        var h264Devices = V4L2.V4L2DeviceManager.GetH264Devices();
        if (!h264Devices.Any())
        {
            throw new Exception("Error: No H.264 capable V4L2 devices found");
        }

        var selectedDevice = h264Devices.First();
        logger.LogInformation("Using V4L2 device: {Driver} - {Card}",
            selectedDevice.DriverName, selectedDevice.CardName);

        var v4L2Device = V4L2DeviceFactory.Open(selectedDevice.DevicePath);
        if (v4L2Device == null)
        {
            throw new Exception($"Error: Failed to open V4L2 device at '{selectedDevice.DevicePath}'");
        }

        return (v4L2Device, selectedDevice);
    }

    private static MediaDevice GetMediaDevice()
    {
        var mediaDevice = MediaDevice.Open("/dev/media0");
        if (mediaDevice == null)
        {
            throw new Exception("Not able to open /dev/media0");
        }

        return mediaDevice;
    }
}