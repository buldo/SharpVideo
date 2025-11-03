using System.Diagnostics;
using System.Runtime.Versioning;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.SDL3;
using Microsoft.Extensions.Logging;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
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
        Logger.LogInformation("Starting hybrid SDL3 + DRM ImGui application");
        Logger.LogInformation("SDL3 provides ImGui infrastructure, DRM provides high-FPS rendering without vsync");

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

            // Setup DMA buffer allocator and manager
            var allocator = DmaBuffersAllocator.Create();
            var buffersManager = new DrmBufferManager(
                drmDevice,
                allocator,
                [KnownPixelFormats.DRM_FORMAT_ARGB8888, KnownPixelFormats.DRM_FORMAT_NV12],
                LoggerFactory.CreateLogger<DrmBufferManager>());

            // Create DRM presenter for primary plane
            var presenter = DrmPresenter.Create(
                drmDevice,
                Width,
                Height,
                buffersManager,
                KnownPixelFormats.DRM_FORMAT_ARGB8888,
                KnownPixelFormats.DRM_FORMAT_NV12, // No overlay plane needed
                Logger);

            if (presenter == null)
            {
                Logger.LogError("Failed to create DRM presenter");
                return;
            }

            // Create DRM ImGui renderer with EGL/OpenGL ES
            using var drmRenderer = new DrmImGuiRenderer(
                drmDevice,
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

            try
            {
                RunMainLoop(io, presenter, drmRenderer);
            }
            finally
            {
                ImGuiImplOpenGL3.Shutdown();
                Hexa.NET.ImGui.ImGui.DestroyContext();
                presenter.CleanupDisplay();
                drmDevice.Dispose();
            }
        }

        SDL.Quit();
        Logger.LogInformation("Application exited successfully");
    }

    private static void RunMainLoop(
        ImGuiIOPtr io,
        DrmPresenter presenter,
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
                        exiting = true;
                    }
                }
            }

            // Start ImGui frame
            io.DeltaTime = deltaTime > 0 ? deltaTime : 1.0f / 60.0f;
            Hexa.NET.ImGui.ImGui.NewFrame();

            // Render ImGui demo window and FPS counter
            RenderImGuiContent(stopwatch.Elapsed, frameCount);

            // End ImGui frame and generate draw data
            Hexa.NET.ImGui.ImGui.Render();
            var drawData = Hexa.NET.ImGui.ImGui.GetDrawData();

            // Get back buffer from DRM presenter
            var backBuffer = presenter.PrimaryPlanePresenter.GetPrimaryPlaneBackBufferDma();

            // Render ImGui directly to DMA buffer using OpenGL ES
// This happens without vsync limitation - maximum speed!
            drmRenderer.RenderToDmaBuffer(backBuffer, drawData);

            // Submit frame for page flip (this is vsync-synchronized)
            // The page flip will happen at next vblank, but we can continue rendering
            if (!presenter.PrimaryPlanePresenter.SwapPrimaryPlaneBuffers())
            {
                Logger.LogWarning("Failed to swap buffers");
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
        Hexa.NET.ImGui.ImGui.BulletText("DRM/GBM: Direct rendering to DMA buffers");
        Hexa.NET.ImGui.ImGui.BulletText("OpenGL ES: GPU-accelerated ImGui rendering");
        Hexa.NET.ImGui.ImGui.BulletText("Page Flip: VSync-synced display update");
        Hexa.NET.ImGui.ImGui.Separator();
        Hexa.NET.ImGui.ImGui.TextColored(new System.Numerics.Vector4(0, 1, 0, 1),
            "Rendering at MAX FPS without vsync limitation!");
        Hexa.NET.ImGui.ImGui.Text("Send SIGTERM/SIGINT to exit");
        Hexa.NET.ImGui.ImGui.End();
    }
}