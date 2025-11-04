using System.Diagnostics;
using System.Runtime.Versioning;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Utils;
using SharpVideo.Linux.Native.C;
using SharpVideo.Linux.Native;

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
   Logger.LogInformation("Starting DRM + GBM ImGui application with libinput");
   Logger.LogInformation("Pure DRM/GBM rendering with libinput for mouse/keyboard/gamepad support");

     // Set environment for DRM
        Environment.SetEnvironmentVariable("EGL_PLATFORM", "drm");

        try
   {
        // Setup ImGui context
            var guiContext = Hexa.NET.ImGui.ImGui.CreateContext();
       Hexa.NET.ImGui.ImGui.SetCurrentContext(guiContext);

            // Setup ImGui config
  var io = Hexa.NET.ImGui.ImGui.GetIO();
       io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
      io.MouseDrawCursor = true; // ImGui will draw cursor

  // Setup ImGui style
      var style = Hexa.NET.ImGui.ImGui.GetStyle();
            style.ScaleAllSizes(1.0f);
  io.DisplaySize = new System.Numerics.Vector2(Width, Height);

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
    var inputAdapter = new ImGuiInputAdapter(inputManager, io);
            Logger.LogInformation("Input system initialized successfully");

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

       Logger.LogInformation("ImGui OpenGL3 backend initialized (libinput for input)");

      // Warmup frame
 Logger.LogInformation("Rendering initial warmup frame...");
     io.DeltaTime = 1.0f / 60.0f;
  ImGuiImplOpenGL3.NewFrame();
  Hexa.NET.ImGui.ImGui.NewFrame();
       RenderImGuiContent(TimeSpan.Zero, 0, 0);
    Hexa.NET.ImGui.ImGui.Render();
         var warmupDrawData = Hexa.NET.ImGui.ImGui.GetDrawData();
            drmRenderer.RenderDrawData(warmupDrawData);

            // Submit first frame to initialize display
   if (drmRenderer.SwapBuffers() && presenter.PrimaryPlanePresenter.SubmitFrame())
   {
    Logger.LogInformation("Warmup frame submitted successfully");
      }
   else
      {
   Logger.LogError("Failed to submit warmup frame");
      return;
     }

     Logger.LogInformation("Warmup frame submitted - waiting for display initialization...");
     Thread.Sleep(100);
Logger.LogInformation("Display initialization completed");

     // Main loop
      try
        {
     Logger.LogInformation("Entering main loop...");
       RunMainLoop(io, presenter, drmRenderer, inputManager, inputAdapter);
   Logger.LogInformation("Main loop completed normally");
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
        catch (Exception ex)
        {
     Logger.LogError(ex, "Fatal error during initialization");
  throw;
   }

        Logger.LogInformation("Application exited successfully");
    }

    private static unsafe void RunMainLoop(
        ImGuiIOPtr io,
     DrmPresenter<DrmPlaneGbmAtomicPresenter, object> presenter,
        DrmImGuiRenderer drmRenderer,
        InputManager inputManager,
        ImGuiInputAdapter inputAdapter)
    {
   Logger.LogInformation("=== RunMainLoop STARTED ===");

  var stopwatch = Stopwatch.StartNew();
   var frameCount = 0;
   var droppedFrames = 0;
        var lastFpsTime = stopwatch.Elapsed;
      var lastFrameTime = stopwatch.Elapsed;
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
var deltaTime = (float)(currentTime - lastFrameTime).TotalSeconds;
   lastFrameTime = currentTime;

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

      // Update ImGui input state
   inputAdapter.UpdateImGuiInput();

       // ImGui frame preparation
 io.DeltaTime = deltaTime > 0 ? deltaTime : 1.0f / 60.0f;
    ImGuiImplOpenGL3.NewFrame();
 Hexa.NET.ImGui.ImGui.NewFrame();

      RenderImGuiContent(stopwatch.Elapsed, frameCount, droppedFrames);

     Hexa.NET.ImGui.ImGui.Render();
     var drawData = Hexa.NET.ImGui.ImGui.GetDrawData();

   // Always render ImGui (GPU work, no blocking)
   drmRenderer.RenderDrawData(drawData);

      // Try to swap and submit frame (may be dropped if queue is full)
       if (drmRenderer.SwapBuffers())
    {
      // eglSwapBuffers succeeded, now try to submit to page flip thread
          if (presenter.PrimaryPlanePresenter.SubmitFrame())
       {
  // Frame submitted successfully
     frameCount++;
      }
        else
    {
           // Frame dropped - queue already full (rendering faster than display)
   droppedFrames++;
   }
        }
        else
     {
       // eglSwapBuffers failed (shouldn't happen with frame dropping, but handle it)
    Logger.LogWarning("eglSwapBuffers failed on frame {Frame}", frameCount + droppedFrames);
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
        Hexa.NET.ImGui.ImGui.Text("Press ESC or send SIGTERM/SIGINT to exit");
        Hexa.NET.ImGui.ImGui.End();
    }
}
