using System.Diagnostics;
using System.Net;
using System.Runtime.Versioning;
using System.Text;

using Hexa.NET.ImGui;
using Microsoft.Extensions.Logging;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.C;
using SharpVideo.Utils;
using SharpVideo.Utils.Buffers;
using SharpVideo.V4L2;
using SharpVideo.V4L2Decoding.Models;
using SharpVideo.V4L2Decoding.Services;
using SharpVideo.ImGui;

namespace SharpVideo.RtpPlayerDemo;

/// <summary>
/// RTP H.264 Player with V4L2 hardware decoding, DRM display using DualPlanePresenter2, and ImGui OSD
/// Receives RTP stream on UDP 0.0.0.0:5600 and displays video with statistics overlay.
/// Uses fence-based synchronization with DualPlanePresenter2.
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
        Logger.LogInformation("=== SharpVideo RTP H.264 Player (DualPlanePresenter2) ===");
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

        // Get device resources
        var resources = drmDevice.GetResources();
        if (resources == null)
        {
            throw new Exception("Failed to get DRM resources");
        }

        // Find connected connector
        var connector = resources.Connectors
            .FirstOrDefault(c => c.Connection == DrmModeConnection.Connected);

        if (connector == null)
        {
            throw new Exception("No connected display found");
        }

        Logger.LogInformation("Found connector: {Type}-{TypeId} (ID: {Id})",
            connector.ConnectorType, connector.ConnectorTypeId, connector.ConnectorId);

        // Get display mode
        var mode = connector.Modes
            .FirstOrDefault(m => m.HDisplay == Width && m.VDisplay == Height)
            ?? connector.Modes.FirstOrDefault();

        if (mode == null)
        {
            throw new Exception("No suitable display mode found");
        }

        Logger.LogInformation("Using mode: {Name} ({Width}x{Height}@{Hz}Hz)",
            mode.Name, mode.HDisplay, mode.VDisplay, mode.VRefresh);

        // Get CRTC ID
        uint crtcId = connector.Encoder?.CrtcId ?? 0;
        if (crtcId == 0)
        {
            crtcId = resources.Crtcs.FirstOrDefault();
            if (crtcId == 0)
            {
                throw new Exception("No CRTC found");
            }
        }

        Logger.LogInformation("Using CRTC: {CrtcId}", crtcId);

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

        // Select planes for video (NV12) and OSD (ARGB8888)
        DualPlaneSelection planeSelection;
        try
        {
            planeSelection = DualPlaneSelector.Select(
                drmDevice,
                crtcId,
                KnownPixelFormats.DRM_FORMAT_NV12.Fourcc,      // Video plane format
                KnownPixelFormats.DRM_FORMAT_ARGB8888.Fourcc,  // OSD plane format
                Logger);

            Logger.LogInformation("Selected planes - Video: {VideoId}, OSD: {OsdId}",
                planeSelection.VideoPlane.Id, planeSelection.OsdPlane.Id);
            Logger.LogInformation("ZPos assignment - Video: {VideoZ} (immutable={VI}), OSD: {OsdZ} (immutable={OI})",
                planeSelection.ZPos.VideoZPos, planeSelection.ZPos.VideoZPosImmutable,
                planeSelection.ZPos.OsdZPos, planeSelection.ZPos.OsdZPosImmutable);
        }
        catch (DrmException ex)
        {
            Logger.LogError(ex, "Failed to select planes");
            throw;
        }

        // Create GBM surface for OSD plane
        var gbmSurface = GbmSurfaceHelper.CreateForRendering(
            gbmDevice,
            Width,
            Height,
            KnownPixelFormats.DRM_FORMAT_ARGB8888,
            Logger);

        // Build presenter configuration
        var presenterConfig = DualPlanePresenterConfig.CreateBuilder()
            .WithVideoPlane(planeSelection.VideoPlane, new PlaneDrawConfiguration(Width, Height))
            .WithOsdPlane(planeSelection.OsdPlane, new PlaneDrawConfiguration(Width, Height))
            .WithCrtc(crtcId)
            .WithConnector(connector.ConnectorId)
            .WithMode(ConvertToNativeMode(mode))
            .WithZPos(planeSelection.ZPos)
            .WithLogger(Logger)
            .Build();

        // Create the dual-plane presenter
        using var presenter = new DualPlanePresenter2(drmDevice, presenterConfig);
        presenter.Start();

        // Setup input manager
        Logger.LogInformation("Initializing input system...");
        using var inputManager = new InputManager((uint)Width, (uint)Height,
            LoggerFactory.CreateLogger<InputManager>());

        // Configure ImGui with the GBM surface
        var imguiConfig = new ImGuiDrmConfiguration
        {
            Width = (uint)Width,
            Height = (uint)Height,
            DrmDevice = drmDevice,
            GbmDevice = gbmDevice,
            GbmSurfaceHandle = gbmSurface.Handle,
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

        // Create decoder pipeline - now uses DualPlanePresenter2 for video
        await using var pipeline = new DecoderPipeline2(
            rtpReceiver,
            decoder,
            presenter,
            LoggerFactory);

        pipeline.Initialize();

        // Create OSD renderer
        var osdRenderer = new OsdRenderer(pipeline.Statistics, rtpReceiver);

        // Start RTP receiver and pipeline
        rtpReceiver.Start();
        await pipeline.StartAsync();

        Logger.LogInformation("RTP receiver started on {Address}:{Port}", BindAddress, BindPort);

        // Warmup ImGui frame
        Logger.LogInformation("Rendering initial warmup frame...");
        if (imguiManager.WarmupFrame(dt => osdRenderer.Render()))
        {
            var warmupBo = GbmSurfaceHelper.LockFrontBuffer(gbmSurface, Logger);
            if (warmupBo != 0)
            {
                var releasedBuffers = new nint[4];
                var (replacedBo, _) = presenter.SetOsdBuffer(warmupBo, releasedBuffers);
                if (replacedBo != 0)
                {
                    GbmSurfaceHelper.ReleaseBuffer(gbmSurface, replacedBo);
                }
                Logger.LogInformation("Warmup frame submitted successfully");
            }
        }

        Thread.Sleep(100);

        // Main loop
        await RunMainLoopAsync(imguiManager, presenter, gbmSurface, inputManager, osdRenderer, pipeline.Statistics, cancellationToken);

        // Cleanup
        await pipeline.StopAsync();
        presenter.Stop();

        // Drain any remaining OSD buffers
        var finalReleasedBuffers = new nint[4];
        var releasedCount = presenter.GetReleasedOsdBuffers(finalReleasedBuffers);
        for (int i = 0; i < releasedCount; i++)
        {
            GbmSurfaceHelper.ReleaseBuffer(gbmSurface, finalReleasedBuffers[i]);
        }

        gbmSurface.Dispose();
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
        DualPlanePresenter2 presenter,
        GbmSurface gbmSurface,
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
        var releasedOsdBuffers = new nint[4];

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
                    // Lock front buffer from GBM surface
                    var osdBo = GbmSurfaceHelper.LockFrontBuffer(gbmSurface, Logger);
                    if (osdBo != 0)
                    {
                        // Submit to presenter
                        var (replacedBo, releasedCount) = presenter.SetOsdBuffer(osdBo, releasedOsdBuffers);

                        // Release replaced buffer
                        if (replacedBo != 0)
                        {
                            GbmSurfaceHelper.ReleaseBuffer(gbmSurface, replacedBo);
                        }

                        // Release any additional buffers
                        for (int i = 0; i < releasedCount; i++)
                        {
                            GbmSurfaceHelper.ReleaseBuffer(gbmSurface, releasedOsdBuffers[i]);
                        }

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