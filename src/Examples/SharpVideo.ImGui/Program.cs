using System.Diagnostics;
using System.Runtime.Versioning;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.SDL3;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Utils;

namespace SharpVideo.ImGui;

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

    static void Main(string[] args)
    {
        Logger.LogInformation("Starting hybrid SDL3 + DRM ImGui application with GBM");
        Logger.LogInformation("SDL3 provides ImGui infrastructure, DRM+GBM provides high-FPS OpenGL ES rendering");

        // Set environment for DRM
        Environment.SetEnvironmentVariable("EGL_PLATFORM", "drm");

        // Initialize SDL3 for event handling and input (no window needed for DRM)
        SDL.Init(SDLInitFlags.Events);

        unsafe
        {
            // Setup ImGui context
            var guiContext = Hexa.NET.ImGui.ImGui.CreateContext();
            Hexa.NET.ImGui.ImGui.SetCurrentContext(guiContext);

            // Setup ImGui config
            var io = Hexa.NET.ImGui.ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            io.MouseDrawCursor = true;

            // Setup ImGui style
            var style = Hexa.NET.ImGui.ImGui.GetStyle();
            style.ScaleAllSizes(1.0f);
            io.DisplaySize = new System.Numerics.Vector2(Width, Height);

            // Open DRM device
            var drmDevice = DrmUtils.OpenDrmDevice(Logger);
            drmDevice.EnableDrmCapabilities(Logger);

            // Create GBM device for OpenGL ES rendering
            var gbmDevice = GbmDevice.CreateFromDrmDevice(drmDevice);
            Logger.LogInformation("Created GBM device for OpenGL ES rendering");

            // Create DRM presenter with GBM buffers for OpenGL ES
            var presenter = DrmPresenter<DrmPlaneGbmPresenter, object>.CreateWithGbmBuffers<object>(
                drmDevice,
                Width,
                Height,
                gbmDevice,
                KnownPixelFormats.DRM_FORMAT_ARGB8888,
                Logger);

            if (presenter == null)
            {
                Logger.LogError("Failed to create DRM presenter");
                return;
            }

            // Create DRM ImGui renderer with EGL/OpenGL ES using the GBM surface
            using var drmRenderer = new DrmImGuiRenderer(
                gbmDevice,
                presenter.PrimaryPlanePresenter.GetNativeGbmSurfaceHandle(),
                Width,
                Height,
                LoggerFactory.CreateLogger<DrmImGuiRenderer>());

            // Initialize ImGui OpenGL3 backend
            ImGuiImplOpenGL3.SetCurrentContext(guiContext);
            if (!ImGuiImplOpenGL3.Init("#version 300 es"))
            {
                Logger.LogError("Failed to init ImGui Impl OpenGL3");
                SDL.Quit();
                return;
            }

            Logger.LogInformation("ImGui OpenGL3 backend initialized");

            // Render an initial "warmup" frame to initialize the pipeline
            Logger.LogInformation("Rendering initial warmup frame...");
            io.DeltaTime = 1.0f / 60.0f;
            Hexa.NET.ImGui.ImGui.NewFrame();
            RenderImGuiContent(TimeSpan.Zero, 0);
            Hexa.NET.ImGui.ImGui.Render();
            var warmupDrawData = Hexa.NET.ImGui.ImGui.GetDrawData();
            drmRenderer.RenderToGbmSurface(warmupDrawData);

            // First SwapBuffers will initialize DRM display
            if (!presenter.PrimaryPlanePresenter.SwapBuffers())
            {
                Logger.LogError("Failed to swap buffers on warmup frame");
                SDL.Quit();
                return;
            }

            Logger.LogInformation("Warmup frame completed successfully");

            try
            {
                RunMainLoop(io, presenter, drmRenderer);
            }
            finally
            {
                ImGuiImplOpenGL3.Shutdown();
                Hexa.NET.ImGui.ImGui.DestroyContext();
                presenter.CleanupDisplay();
                gbmDevice.Dispose();
                drmDevice.Dispose();
            }
        }

        SDL.Quit();
        Logger.LogInformation("Application exited successfully");
    }

    private static void RunMainLoop(
        ImGuiIOPtr io,
        DrmPresenter<DrmPlaneGbmPresenter, object> presenter,
        DrmImGuiRenderer drmRenderer)
    {
        var stopwatch = Stopwatch.StartNew();
        var frameCount = 0;
        var lastFpsTime = stopwatch.Elapsed;
        var lastFrameTime = stopwatch.Elapsed;
        var exiting = false;

        Logger.LogInformation("Starting main loop - rendering at maximum speed without vsync");

        while (!exiting)
        {
            try
            {
                var currentTime = stopwatch.Elapsed;
                var deltaTime = (float)(currentTime - lastFrameTime).TotalSeconds;
                lastFrameTime = currentTime;

                // Poll SDL events for input
                SDL.PumpEvents();

                // Simple quit handling - just check for quit event
                // (In real app, you'd want proper keyboard/mouse handling via SDL backends)
                unsafe
                {
                    Hexa.NET.SDL3.SDLEvent sdlEvent;
                    while (SDL.PollEvent(&sdlEvent))
                    {
                        if (sdlEvent.Type == (uint)SDLEventType.Quit)
                        {
                            Logger.LogInformation("Received quit event, exiting");
                            exiting = true;
                        }
                    }
                }

                if (exiting)
                    break;

                // Start ImGui frame
                io.DeltaTime = deltaTime > 0 ? deltaTime : 1.0f / 60.0f;
                Hexa.NET.ImGui.ImGui.NewFrame();

                // Render ImGui demo window and FPS counter
                RenderImGuiContent(stopwatch.Elapsed, frameCount);

                // End ImGui frame and generate draw data
                Hexa.NET.ImGui.ImGui.Render();
                var drawData = Hexa.NET.ImGui.ImGui.GetDrawData();

                // Render ImGui to the GBM surface using OpenGL ES
                // This happens without vsync limitation - maximum speed!
                drmRenderer.RenderToGbmSurface(drawData);

                // Swap GBM buffers and present (this is vsync-synchronized via page flip)
                if (!presenter.PrimaryPlanePresenter.SwapBuffers())
                {
                    Logger.LogWarning("Failed to swap buffers on frame {Frame}", frameCount);
                }
                else
                {
                    Logger.LogTrace("Frame {Frame} presented successfully", frameCount);
                }

                frameCount++;

                // Log FPS every second
                if ((currentTime - lastFpsTime).TotalSeconds >= 1.0)
                {
                    var fps = frameCount / (currentTime - lastFpsTime).TotalSeconds;
                    Logger.LogInformation("FPS: {Fps:F2} (rendering without vsync limitation)", fps);
                    frameCount = 0;
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

    private static void RenderImGuiContent(TimeSpan elapsed, int frameCount)
    {
        // Show demo window
        Hexa.NET.ImGui.ImGui.ShowDemoWindow();

        // Show custom FPS/Info window
        Hexa.NET.ImGui.ImGui.Begin("Performance Info");
        Hexa.NET.ImGui.ImGui.Text($"Frame: {frameCount}");
        Hexa.NET.ImGui.ImGui.Text($"Time: {elapsed.TotalSeconds:F2}s");
        Hexa.NET.ImGui.ImGui.Text($"FPS: {Hexa.NET.ImGui.ImGui.GetIO().Framerate:F1}");
        Hexa.NET.ImGui.ImGui.Separator();
        Hexa.NET.ImGui.ImGui.Text("Hybrid Rendering:");
        Hexa.NET.ImGui.ImGui.BulletText("SDL3: Input & ImGui infrastructure");
        Hexa.NET.ImGui.ImGui.BulletText("DRM/GBM: Direct rendering with GPU buffers");
        Hexa.NET.ImGui.ImGui.BulletText("OpenGL ES: GPU-accelerated ImGui rendering");
        Hexa.NET.ImGui.ImGui.BulletText("Page Flip: VSync-synced display update");
        Hexa.NET.ImGui.ImGui.Separator();
        Hexa.NET.ImGui.ImGui.TextColored(new System.Numerics.Vector4(0, 1, 0, 1),
            "Rendering at MAX FPS without vsync limitation!");
        Hexa.NET.ImGui.ImGui.Text("Send SIGTERM/SIGINT to exit");
        Hexa.NET.ImGui.ImGui.End();
    }
}