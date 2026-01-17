using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

using Hexa.NET.ImGui;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Utils;
using SharpVideo.Utils.Buffers;
using SharpVideo.Linux.Native.C;
using SharpVideo.Linux.Native;
using SharpVideo.ImGui;

namespace SharpVideo.ImGuiDemo;

/// <summary>
/// ImGui demo using DualPlanePresenter2 in OSD-only mode.
/// Demonstrates pure ImGui rendering without video plane.
/// </summary>
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
        Logger.LogInformation("=== ImGui Demo (DualPlanePresenter2 OSD-only mode) ===");
        Logger.LogInformation("Using DualPlanePresenter2 with fence-based synchronization");

        // Set environment for DRM
        Environment.SetEnvironmentVariable("EGL_PLATFORM", "drm");

        try
        {
            // Open DRM device
            Logger.LogDebug("Opening DRM device...");
            var drmDevice = DrmUtils.OpenDrmDevice(Logger);
            drmDevice.EnableDrmCapabilities(Logger);

            // Get device resources
            var resources = drmDevice.GetResources();
            if (resources == null)
            {
                Logger.LogError("Failed to get DRM resources");
                return;
            }

            // Find connected connector
            var connector = resources.Connectors
                .FirstOrDefault(c => c.Connection == DrmModeConnection.Connected);

            if (connector == null)
            {
                Logger.LogError("No connected display found");
                return;
            }

            Logger.LogInformation("Found connector: {Type}-{TypeId} (ID: {Id})",
                connector.ConnectorType, connector.ConnectorTypeId, connector.ConnectorId);

            // Get display mode
            var mode = connector.Modes
                .FirstOrDefault(m => m.HDisplay == Width && m.VDisplay == Height)
                ?? connector.Modes.FirstOrDefault();

            if (mode == null)
            {
                Logger.LogError("No suitable display mode found");
                return;
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
                    Logger.LogError("No CRTC found");
                    return;
                }
            }

            Logger.LogInformation("Using CRTC: {CrtcId}", crtcId);

            // Find the CRTC index for filtering planes
            var crtcList = resources.Crtcs.ToList();
            var crtcIndex = crtcList.IndexOf(crtcId);
            if (crtcIndex < 0)
            {
                Logger.LogError("CRTC {CrtcId} not found in resources", crtcId);
                return;
            }

            var crtcMask = 1u << crtcIndex;

            // Find OSD plane (ARGB8888 for ImGui)
            var osdPlane = resources.Planes
                .Where(p => (p.PossibleCrtcs & crtcMask) != 0)
                .Where(p => p.Formats.Contains(KnownPixelFormats.DRM_FORMAT_ARGB8888.Fourcc))
                .FirstOrDefault();

            if (osdPlane == null)
            {
                Logger.LogError("No suitable OSD plane found for ARGB8888 format");
                return;
            }

            Logger.LogInformation("Selected OSD plane: {PlaneId}", osdPlane.Id);

            // Create GBM device for ImGui rendering
            var gbmDevice = GbmDevice.CreateFromDrmDevice(drmDevice);
            Logger.LogInformation("Created GBM device for ImGui rendering");

            // Create GBM surface for OSD plane
            var gbmSurface = GbmSurfaceHelper.CreateForRendering(
                gbmDevice,
                Width,
                Height,
                KnownPixelFormats.DRM_FORMAT_ARGB8888,
                Logger);

            // Build presenter configuration (OSD-only mode)
            var presenterConfig = DualPlanePresenterConfig.CreateBuilder()
                .WithOsdPlane(osdPlane, new PlaneDrawConfiguration(Width, Height))
                .WithCrtc(crtcId)
                .WithConnector(connector.ConnectorId)
                .WithMode(ConvertToNativeMode(mode))
                .WithLogger(Logger)
                .Build();

            // Create the dual-plane presenter
            using var presenter = new DualPlanePresenter2(drmDevice, presenterConfig);
            presenter.Start();

            // Create input manager with libinput
            Logger.LogInformation("Initializing libinput for input devices...");
            using var inputManager = new InputManager((uint)Width, (uint)Height,
                LoggerFactory.CreateLogger<InputManager>());
            Logger.LogInformation("Input system initialized successfully");

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

            Logger.LogInformation("ImGui manager initialized successfully");

            // Warmup frame
            Logger.LogInformation("Rendering initial warmup frame...");
            if (imguiManager.WarmupFrame(dt => RenderImGuiContent(TimeSpan.Zero, 0, 0)))
            {
                // Lock front buffer and submit to presenter
                var warmupBo = GbmSurfaceHelper.LockFrontBuffer(gbmSurface, Logger);
                if (warmupBo != 0)
                {
                    var releasedOsdBuffers = new nint[4];
                    var (replacedBo, _) = presenter.SetOsdBuffer(warmupBo, releasedOsdBuffers);
                    if (replacedBo != 0)
                    {
                        GbmSurfaceHelper.ReleaseBuffer(gbmSurface, replacedBo);
                    }
                    Logger.LogInformation("Warmup frame submitted successfully");
                }
            }

            Logger.LogInformation("Display initialization completed");
            Thread.Sleep(100);

            // Main loop
            try
            {
                Logger.LogInformation("Entering main loop...");
                RunMainLoop(imguiManager, presenter, gbmSurface, inputManager);
                Logger.LogInformation("Main loop completed normally");
            }
            finally
            {
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
        DualPlanePresenter2 presenter,
        GbmSurface gbmSurface,
        InputManager inputManager)
    {
        Logger.LogInformation("=== RunMainLoop STARTED ===");

        var stopwatch = Stopwatch.StartNew();
        var frameCount = 0;
        var droppedFrames = 0;
        var lastFpsTime = stopwatch.Elapsed;
        var exiting = false;

        Logger.LogInformation("Starting main loop - rendering at maximum FPS");
        Logger.LogInformation("Press ESC key or Ctrl+C to exit");

        // Setup poll for input events
        var inputFd = inputManager.GetFileDescriptor();
        var releasedOsdBuffers = new nint[4];

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
                }

                if (exiting) break;

                // Render frame using ImGui manager
                var frameRendered = imguiManager.RenderFrame(dt =>
                    RenderImGuiContent(stopwatch.Elapsed, frameCount, droppedFrames));

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

                        // Release any additional buffers that finished displaying
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
                else
                {
                    droppedFrames++;
                }

                // Log FPS
                if ((currentTime - lastFpsTime).TotalSeconds >= 1.0)
                {
                    var totalFrames = frameCount + droppedFrames;
                    var renderFps = totalFrames / (currentTime - lastFpsTime).TotalSeconds;
                    var displayFps = frameCount / (currentTime - lastFpsTime).TotalSeconds;
                    var dropRate = totalFrames > 0 ? droppedFrames / (double)totalFrames * 100.0 : 0;

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
        Hexa.NET.ImGui.ImGui.Text("DualPlanePresenter2 Architecture:");
        Hexa.NET.ImGui.ImGui.BulletText("OSD-only mode (no video plane)");
        Hexa.NET.ImGui.ImGui.BulletText("Fence-based synchronization");
        Hexa.NET.ImGui.ImGui.BulletText("Dedicated commit thread");
        Hexa.NET.ImGui.ImGui.BulletText("Native Linux input via libinput");
        Hexa.NET.ImGui.ImGui.Separator();
        Hexa.NET.ImGui.ImGui.TextColored(new System.Numerics.Vector4(0, 1, 0, 1),
            "Max FPS rendering with smart frame dropping!");
        Hexa.NET.ImGui.ImGui.TextColored(new System.Numerics.Vector4(0, 1, 1, 1),
            "Full native input support via libinput!");
        Hexa.NET.ImGui.ImGui.TextColored(new System.Numerics.Vector4(1, 1, 0, 1),
            "Using DualPlanePresenter2 with OUT_FENCE_PTR!");
        Hexa.NET.ImGui.ImGui.Text("Press ESC or send SIGTERM/SIGINT to exit");
        Hexa.NET.ImGui.ImGui.End();
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
