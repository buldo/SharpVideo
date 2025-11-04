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

    static unsafe void Main(string[] args)
    {
        Logger.LogInformation("Starting hybrid SDL3 + DRM ImGui application with GBM");
        Logger.LogInformation("SDL3 provides ImGui infrastructure, DRM+GBM provides high-FPS OpenGL ES rendering");

        // Set environment for DRM
        Environment.SetEnvironmentVariable("EGL_PLATFORM", "drm");

        // Initialize SDL3 with video subsystem for hidden window
 Logger.LogDebug("Initializing SDL3 with video subsystem...");
    if (!SDL.Init(SDLInitFlags.Video | SDLInitFlags.Events))
        {
  var errorPtr = SDL.GetError();
  var error = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)errorPtr) ?? "Unknown error";
         Logger.LogError("Failed to initialize SDL3: {Error}", error);
 return;
  }
        Logger.LogInformation("SDL3 initialized successfully");

        // Create hidden window for mouse input
  Logger.LogDebug("Creating hidden SDL window for input handling...");
        var hiddenWindow = SDL.CreateWindow(
   "SharpVideo ImGui (Hidden)"u8,
            Width,
            Height,
            SDLWindowFlags.Hidden | SDLWindowFlags.Borderless);

 if (hiddenWindow == null)
      {
       var errorPtr = SDL.GetError();
            var error = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)errorPtr) ?? "Unknown error";
     Logger.LogError("Failed to create hidden SDL window: {Error}", error);
            SDL.Quit();
            return;
        }
 Logger.LogInformation("Created hidden SDL window for input handling");

        try
        {
            // Setup ImGui context
         var guiContext = Hexa.NET.ImGui.ImGui.CreateContext();
  Hexa.NET.ImGui.ImGui.SetCurrentContext(guiContext);

 // Setup ImGui config
     var io = Hexa.NET.ImGui.ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
     io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

  // Setup ImGui style
         var style = Hexa.NET.ImGui.ImGui.GetStyle();
   style.ScaleAllSizes(1.0f);
            io.DisplaySize = new System.Numerics.Vector2(Width, Height);

            // Initialize SDL3 backend for ImGui
            Logger.LogDebug("Initializing SDL3 backend for ImGui...");
            ImGuiImplSDL3.SetCurrentContext(guiContext);

            var sdl3InitSuccess = false;
   try
    {
     var sdlWindowPtr = new Hexa.NET.ImGui.Backends.SDL3.SDLWindowPtr(
        (Hexa.NET.ImGui.Backends.SDL3.SDLWindow*)(void*)hiddenWindow);
    sdl3InitSuccess = ImGuiImplSDL3.InitForOther(sdlWindowPtr);
       Logger.LogDebug("SDL3 backend Init returned: {Success}", sdl3InitSuccess);
            }
            catch (Exception ex)
      {
  Logger.LogWarning(ex, "SDL3 backend Init threw exception");
            }

            if (!sdl3InitSuccess)
         {
         Logger.LogWarning("SDL3 backend initialization failed - mouse cursor will not be rendered");
            io.MouseDrawCursor = false;
          }
            else
  {
            Logger.LogInformation("ImGui SDL3 backend initialized successfully (input enabled)");
     io.MouseDrawCursor = true;
            }

 // Open DRM device
Logger.LogDebug("Opening DRM device...");
  var drmDevice = DrmUtils.OpenDrmDevice(Logger);
      drmDevice.EnableDrmCapabilities(Logger);

            // Create GBM device
      var gbmDevice = GbmDevice.CreateFromDrmDevice(drmDevice);
            Logger.LogInformation("Created GBM device for OpenGL ES rendering");

            // Create DRM presenter
        var presenter = DrmPresenter<DrmPlaneGbmPresenter, object>.CreateWithGbmBuffers<object>(
     drmDevice, Width, Height, gbmDevice,
                KnownPixelFormats.DRM_FORMAT_ARGB8888, Logger);

    if (presenter == null)
      {
  Logger.LogError("Failed to create DRM presenter");
                return;
    }

     // Create DRM ImGui renderer
            using var drmRenderer = new DrmImGuiRenderer(
         gbmDevice,
 presenter.PrimaryPlanePresenter.GetNativeGbmSurfaceHandle(),
   Width, Height,
       LoggerFactory.CreateLogger<DrmImGuiRenderer>());

    // Initialize ImGui OpenGL3 backend
          ImGuiImplOpenGL3.SetCurrentContext(guiContext);
     if (!ImGuiImplOpenGL3.Init("#version 300 es"))
      {
     Logger.LogError("Failed to init ImGui Impl OpenGL3");
      return;
    }

            Logger.LogInformation("ImGui OpenGL3 and SDL3 backends initialized");

            // Warmup frame
       Logger.LogInformation("Rendering initial warmup frame...");
    io.DeltaTime = 1.0f / 60.0f;
            if (sdl3InitSuccess) ImGuiImplSDL3.NewFrame();
  ImGuiImplOpenGL3.NewFrame();
     Hexa.NET.ImGui.ImGui.NewFrame();
  RenderImGuiContent(TimeSpan.Zero, 0);
            Hexa.NET.ImGui.ImGui.Render();
  var warmupDrawData = Hexa.NET.ImGui.ImGui.GetDrawData();
      drmRenderer.RenderToGbmSurface(warmupDrawData);

         if (!presenter.PrimaryPlanePresenter.SwapBuffers())
       {
    Logger.LogError("Failed to swap buffers on warmup frame");
 return;
            }

            Logger.LogInformation("Warmup frame completed successfully");

       // Main loop
      try
     {
            Logger.LogInformation("Entering main loop...");
         RunMainLoop(io, presenter, drmRenderer, sdl3InitSuccess);
      Logger.LogInformation("Main loop completed normally");
  }
     finally
    {
          ImGuiImplOpenGL3.Shutdown();
       if (sdl3InitSuccess) ImGuiImplSDL3.Shutdown();
       Hexa.NET.ImGui.ImGui.DestroyContext();
                presenter.CleanupDisplay();
    gbmDevice.Dispose();
        drmDevice.Dispose();
        }
        }
        finally
   {
          Logger.LogDebug("Destroying hidden SDL window...");
          SDL.DestroyWindow(hiddenWindow);
        }

        SDL.Quit();
        Logger.LogInformation("Application exited successfully");
}

    private static unsafe void RunMainLoop(
        ImGuiIOPtr io,
  DrmPresenter<DrmPlaneGbmPresenter, object> presenter,
        DrmImGuiRenderer drmRenderer,
        bool sdl3BackendEnabled)
    {
        Logger.LogInformation("=== RunMainLoop STARTED ===");
        Logger.LogDebug("SDL3 backend enabled: {Enabled}", sdl3BackendEnabled);

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

   // Poll SDL events
      SDL.PumpEvents();
      Hexa.NET.SDL3.SDLEvent sdlEvent;
         while (SDL.PollEvent(&sdlEvent))
         {
     if (sdl3BackendEnabled)
          {
        var imgui_sdlEvent = (Hexa.NET.ImGui.Backends.SDL3.SDLEvent*)(&sdlEvent);
 var eventPtr = new Hexa.NET.ImGui.Backends.SDL3.SDLEventPtr(imgui_sdlEvent);
           ImGuiImplSDL3.ProcessEvent(eventPtr);
      }

       if (sdlEvent.Type == (uint)SDLEventType.Quit)
            {
             Logger.LogInformation("Received quit event, exiting");
               exiting = true;
           }
      }

        if (exiting) break;

           // ImGui frame
        io.DeltaTime = deltaTime > 0 ? deltaTime : 1.0f / 60.0f;
        if (sdl3BackendEnabled) ImGuiImplSDL3.NewFrame();
      ImGuiImplOpenGL3.NewFrame();
             Hexa.NET.ImGui.ImGui.NewFrame();

         RenderImGuiContent(stopwatch.Elapsed, frameCount);

        Hexa.NET.ImGui.ImGui.Render();
   var drawData = Hexa.NET.ImGui.ImGui.GetDrawData();

         drmRenderer.RenderToGbmSurface(drawData);

            if (!presenter.PrimaryPlanePresenter.SwapBuffers())
              {
             Logger.LogWarning("Failed to swap buffers on frame {Frame}", frameCount);
                }

frameCount++;

     // Log FPS
                if ((currentTime - lastFpsTime).TotalSeconds >= 1.0)
        {
   var fps = frameCount / (currentTime - lastFpsTime).TotalSeconds;
      Logger.LogInformation("FPS: {Fps:F2}", fps);
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
        Hexa.NET.ImGui.ImGui.ShowDemoWindow();

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
