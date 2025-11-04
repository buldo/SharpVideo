using System.Diagnostics;
using System.Runtime.Versioning;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImGui.Backends.SDL3;
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
            
   // We're rendering to DRM directly, but we still need mouse input
     // Use SDL3 backend for input handling only (no window)
    io.MouseDrawCursor = true;

            // Setup ImGui style
            var style = Hexa.NET.ImGui.ImGui.GetStyle();
            style.ScaleAllSizes(1.0f);
            io.DisplaySize = new System.Numerics.Vector2(Width, Height);

            // Initialize SDL3 backend for ImGui (for input handling)
  // We pass null window since we're rendering to DRM without SDL window
 ImGuiImplSDL3.SetCurrentContext(guiContext);
if (!ImGuiImplSDL3.InitForOther(default))
  {
   Logger.LogError("Failed to init ImGui SDL3 backend");
 SDL.Quit();
 return;
 }
   Logger.LogInformation("ImGui SDL3 backend initialized (input only)");

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



            Logger.LogInformation("ImGui OpenGL3 and SDL3 backends initialized");

            // Render an initial "warmup" frame to initialize the pipeline
            Logger.LogInformation("Rendering initial warmup frame...");
            io.DeltaTime = 1.0f / 60.0f;

  // ImGui backends require NewFrame calls in correct order:
            // 1. Platform backend NewFrame (SDL3) - processes input
 // 2. Renderer backend NewFrame (OpenGL3) - prepares GPU resources
   // 3. ImGui core NewFrame - starts frame
   ImGuiImplSDL3.NewFrame();
 ImGuiImplOpenGL3.NewFrame();
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

            // Reset ImGui state for main loop
            Logger.LogDebug("Resetting stopwatch and preparing for main loop");
            var mainLoopStopwatch = Stopwatch.StartNew();

            try
            {
                Logger.LogInformation("Entering main loop...");
                RunMainLoop(io, presenter, drmRenderer);
                Logger.LogInformation("Main loop completed normally");
            }
            finally
            {
     ImGuiImplOpenGL3.Shutdown();
            ImGuiImplSDL3.Shutdown();
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
        Logger.LogInformation("=== RunMainLoop STARTED ===");
        Logger.LogDebug("Creating stopwatch for main loop timing");

        var stopwatch = Stopwatch.StartNew();
        var frameCount = 0;
        var lastFpsTime = stopwatch.Elapsed;
        var lastFrameTime = stopwatch.Elapsed;
        var exiting = false;

        Logger.LogInformation("Starting main loop - rendering at maximum speed without vsync");
        Logger.LogDebug("Initial values: frameCount=0, exiting=false");

        while (!exiting)
        {
            try
            {
                Logger.LogDebug("=== Frame {Frame} START ===", frameCount);
                var currentTime = stopwatch.Elapsed;
                var deltaTime = (float)(currentTime - lastFrameTime).TotalSeconds;
                lastFrameTime = currentTime;

                Logger.LogDebug("Frame {Frame}: deltaTime={DeltaTime}s", frameCount, deltaTime);

                // Poll SDL events for input
                Logger.LogDebug("Frame {Frame}: Polling SDL events...", frameCount);
                SDL.PumpEvents();
                Logger.LogDebug("Frame {Frame}: SDL events polled", frameCount);

   // Process SDL events through ImGui backend
  unsafe
  {
  Hexa.NET.SDL3.SDLEvent sdlEvent;
    while (SDL.PollEvent(&sdlEvent))
    {
   // Let ImGui process the event (for mouse/keyboard/quit)
   // Cast to Hexa.NET ImGui backend's SDLEvent type
      var imgui_sdlEvent = (Hexa.NET.ImGui.Backends.SDL3.SDLEvent*)(&sdlEvent);
var eventPtr = new Hexa.NET.ImGui.Backends.SDL3.SDLEventPtr(imgui_sdlEvent);
       ImGuiImplSDL3.ProcessEvent(eventPtr);
       
 // Check for quit event to exit main loop
     if (sdlEvent.Type == (uint)SDLEventType.Quit)
   {
       Logger.LogInformation("Received quit event, exiting");
 exiting = true;
         }
   }
       }

  Logger.LogDebug("Frame {Frame}: Processed SDL events", frameCount);

                if (exiting)
                    break;

                // Start ImGui frame
                Logger.LogDebug("Frame {Frame}: Setting deltaTime and calling NewFrame...", frameCount);
                io.DeltaTime = deltaTime > 0 ? deltaTime : 1.0f / 60.0f;

                // ImGui backends require NewFrame calls in correct order:
                // 1. Platform backend NewFrame (SDL3) - processes input
                // 2. Renderer backend NewFrame (OpenGL3) - prepares GPU resources
                // 3. ImGui core NewFrame - starts frame
                ImGuiImplSDL3.NewFrame();
                ImGuiImplOpenGL3.NewFrame();
                Hexa.NET.ImGui.ImGui.NewFrame();
                Logger.LogDebug("Frame {Frame}: ImGui NewFrame completed", frameCount);

                // Render ImGui demo window and FPS counter
                Logger.LogDebug("Frame {Frame}: Rendering ImGui content...", frameCount);
                RenderImGuiContent(stopwatch.Elapsed, frameCount);
                Logger.LogDebug("Frame {Frame}: ImGui content rendered", frameCount);

                // End ImGui frame and generate draw data
                Logger.LogDebug("Frame {Frame}: Calling ImGui Render...", frameCount);
                Hexa.NET.ImGui.ImGui.Render();
                Logger.LogDebug("Frame {Frame}: ImGui Render completed", frameCount);
                var drawData = Hexa.NET.ImGui.ImGui.GetDrawData();
                Logger.LogDebug("Frame {Frame}: Got draw data", frameCount);

                // Render ImGui to the GBM surface using OpenGL ES
                // This happens without vsync limitation - maximum speed!
                Logger.LogDebug("Frame {Frame}: Rendering to GBM surface...", frameCount);
                drmRenderer.RenderToGbmSurface(drawData);
                Logger.LogDebug("Frame {Frame}: GBM surface render completed", frameCount);

                // Swap GBM buffers and present (this is vsync-synchronized via page flip)
                Logger.LogDebug("Frame {Frame}: Swapping buffers...", frameCount);
                if (!presenter.PrimaryPlanePresenter.SwapBuffers())
                {
                    Logger.LogWarning("Failed to swap buffers on frame {Frame}", frameCount);
                }
                else
                {
                    Logger.LogDebug("Frame {Frame} presented successfully", frameCount);
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

                Logger.LogDebug("=== Frame {Frame} END ===", frameCount - 1);
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