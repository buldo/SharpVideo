using System.Diagnostics;
using System.Runtime.Versioning;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Utils;
using SharpVideo.Linux.Native.C;
using SharpVideo.Linux.Native;
using SharpVideo.ImGui;

namespace SharpVideo.ImGuiDemo;

[SupportedOSPlatform("linux")]
internal class Program
{
    private const int Width = 1920;
    private const int Height = 1080;

    private static readonly ILoggerFactory LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory
        .Create(builder => builder.AddConsole()
#if DEBUG
            .SetMinimumLevel(LogLevel.Debug)
#else
            .SetMinimumLevel(LogLevel.Information)
#endif
        );

    private static readonly ILogger Logger = LoggerFactory.CreateLogger<Program>();

    static unsafe void Main(string[] args)
    {
        Logger.LogInformation("Starting DRM + GBM ImGui application with libinput");
        Logger.LogInformation("Using SharpVideo.ImGui library for ImGui integration");

        // Set environment for DRM
        Environment.SetEnvironmentVariable("EGL_PLATFORM", "drm");

        try
        {
            // Open DRM device
            Logger.LogDebug("Opening DRM device...");
            var drmDevice = DrmUtils.OpenDrmDevice(Logger);
            drmDevice.EnableDrmCapabilities(Logger);

            // Create GBM device
            var gbmDevice = GbmDevice.CreateFromDrmDevice(drmDevice);
            Logger.LogInformation("Created GBM device for OpenGL ES rendering");

            // Create DRM presenter with atomic GBM buffers
            var presenter = DrmPresenter<DrmPlaneGbmAtomicPresenter, object>.CreateWithGbmBuffersAtomic<object>(
                drmDevice, Width, Height, gbmDevice,
                KnownPixelFormats.DRM_FORMAT_ARGB8888, Logger);

            if (presenter == null)
            {
                Logger.LogError("Failed to create DRM presenter");
                return;
            }

            // Create input manager with libinput
            Logger.LogInformation("Initializing libinput for input devices...");
            using var inputManager = new InputManager((uint)Width, (uint)Height,
                LoggerFactory.CreateLogger<InputManager>());
            Logger.LogInformation("Input system initialized successfully");

            // Configure ImGui
            var imguiConfig = new ImGuiDrmConfiguration
            {
                Width = (uint)Width,
                Height = (uint)Height,
                DrmDevice = drmDevice,
                GbmDevice = gbmDevice,
                GbmSurfaceHandle = presenter.PrimaryPlanePresenter.GetNativeGbmSurfaceHandle(),
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

            Logger.LogInformation("ImGui manager initialized successfully");

            // Warmup frame
            Logger.LogInformation("Rendering initial warmup frame...");
            if (imguiManager.WarmupFrame(dt => RenderImGuiContent(TimeSpan.Zero, 0, 0)))
            {
                if (presenter.PrimaryPlanePresenter.SubmitFrame())
                {
                    Logger.LogInformation("Warmup frame submitted successfully");
                }
                else
                {
                    Logger.LogError("Failed to submit warmup frame");
                    return;
                }
            }

            Logger.LogInformation("Warmup frame submitted - waiting for display initialization...");
            Thread.Sleep(100);
            Logger.LogInformation("Display initialization completed");

            // Main loop
            try
            {
                Logger.LogInformation("Entering main loop...");
                RunMainLoop(imguiManager, presenter, inputManager);
                Logger.LogInformation("Main loop completed normally");
            }
            finally
            {
                presenter.Dispose();
                gbmDevice.Dispose();
                drmDevice.Dispose();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Fatal error during initialization");
            throw;
        }

        Logger.LogInformation("Application exited successfully");
    }

    private static unsafe void RunMainLoop(
        ImGuiManager imguiManager,
        DrmPresenter<DrmPlaneGbmAtomicPresenter, object> presenter,
        InputManager inputManager)
    {
        Logger.LogInformation("=== RunMainLoop STARTED ===");

        var stopwatch = Stopwatch.StartNew();
        var frameCount = 0;
        var droppedFrames = 0;
        var lastFpsTime = stopwatch.Elapsed;
        var exiting = false;

        Logger.LogInformation("Starting main loop - rendering at maximum FPS without vsync blocking");
        Logger.LogInformation("Press ESC key or Ctrl+C to exit");

        // Setup poll for input events
        var inputFd = inputManager.GetFileDescriptor();

        while (!exiting)
        {
            try
            {
                var currentTime = stopwatch.Elapsed;

                // Poll input events (non-blocking)
                var pollFd = new PollFd
                {
                    fd = inputFd,
                    events = PollEvents.POLLIN
                };

                var pollResult = Libc.poll(ref pollFd, 1, 0); // 0 timeout = non-blocking
                if (pollResult > 0)
                {
                    inputManager.ProcessEvents();
                }

                // Check for ESC key to exit
                if (inputManager.IsKeyDown(1)) // KEY_ESC = 1
                {
                    Logger.LogInformation("ESC key pressed, exiting");
                    exiting = true;
                }

                if (exiting) break;

                // Render frame using ImGui manager
                var frameRendered = imguiManager.RenderFrame(dt =>
                    RenderImGuiContent(stopwatch.Elapsed, frameCount, droppedFrames));

                if (frameRendered)
                {
                    // Try to submit frame to page flip thread
                    if (presenter.PrimaryPlanePresenter.SubmitFrame())
                    {
                        // Frame submitted successfully
                        frameCount++;
                    }
                    else
                    {
                        // Frame dropped - queue already full
                        droppedFrames++;
                    }
                }
                else
                {
                    // Swap failed (shouldn't happen with frame dropping, but handle it)
                    Logger.LogWarning("Frame swap failed on frame {Frame}", frameCount + droppedFrames);
                    droppedFrames++;
                }

                // Log FPS
                if ((currentTime - lastFpsTime).TotalSeconds >= 1.0)
                {
                    var totalFrames = frameCount + droppedFrames;
                    var renderFps = totalFrames / (currentTime - lastFpsTime).TotalSeconds;
                    var displayFps = frameCount / (currentTime - lastFpsTime).TotalSeconds;
                    var dropRate = droppedFrames / (double)totalFrames * 100.0;

                    Logger.LogInformation(
                        "Render FPS: {RenderFps:F1} | Display FPS: {DisplayFps:F1} | Dropped: {Dropped} ({DropRate:F1}%)",
                        renderFps, displayFps, droppedFrames, dropRate);

                    frameCount = 0;
                    droppedFrames = 0;
                    lastFpsTime = currentTime;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Exception in main loop on frame {Frame}", frameCount);
                exiting = true;
            }
        }

        Logger.LogInformation("Main loop exited after {FrameCount} frames", frameCount);
    }

    private static void RenderImGuiContent(TimeSpan elapsed, int displayedFrames, int droppedFrames)
    {
        Hexa.NET.ImGui.ImGui.ShowDemoWindow();

        Hexa.NET.ImGui.ImGui.Begin("Performance Info");
        Hexa.NET.ImGui.ImGui.Text($"Displayed Frames: {displayedFrames}");
        Hexa.NET.ImGui.ImGui.Text($"Dropped Frames: {droppedFrames}");
        Hexa.NET.ImGui.ImGui.Text($"Total Rendered: {displayedFrames + droppedFrames}");
        Hexa.NET.ImGui.ImGui.Text($"Time: {elapsed.TotalSeconds:F2}s");
        Hexa.NET.ImGui.ImGui.Text($"ImGui FPS: {Hexa.NET.ImGui.ImGui.GetIO().Framerate:F1}");
        Hexa.NET.ImGui.ImGui.Separator();
        Hexa.NET.ImGui.ImGui.Text("Pure DRM/GBM + libinput Architecture:");
        Hexa.NET.ImGui.ImGui.BulletText("Render Thread: Max FPS with frame dropping");
        Hexa.NET.ImGui.ImGui.BulletText("Page Flip Thread: VBlank-synced (60Hz)");
        Hexa.NET.ImGui.ImGui.BulletText("DRM Atomic: Non-blocking page flips");
        Hexa.NET.ImGui.ImGui.BulletText("libinput: Native Linux input");
        Hexa.NET.ImGui.ImGui.BulletText("Latest Frame Only: drops if queue full");
        Hexa.NET.ImGui.ImGui.Separator();
        Hexa.NET.ImGui.ImGui.TextColored(new System.Numerics.Vector4(0, 1, 0, 1),
            "Max FPS rendering with smart frame dropping!");
        Hexa.NET.ImGui.ImGui.TextColored(new System.Numerics.Vector4(0, 1, 1, 1),
            "Full native input support via libinput!");
        Hexa.NET.ImGui.ImGui.TextColored(new System.Numerics.Vector4(1, 1, 0, 1),
            "Using SharpVideo.ImGui library!");
        Hexa.NET.ImGui.ImGui.Text("Press ESC or send SIGTERM/SIGINT to exit");
        Hexa.NET.ImGui.ImGui.End();
    }
}
