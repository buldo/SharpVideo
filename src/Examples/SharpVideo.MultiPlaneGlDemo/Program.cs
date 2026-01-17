using System.Runtime.Versioning;
using System.Text;

using Microsoft.Extensions.Logging;

using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.Gbm;
using SharpVideo.Utils;
using SharpVideo.Utils.Buffers;

namespace SharpVideo.MultiPlaneGlExample;

/// <summary>
/// Multi-plane GL demo using DualPlanePresenter2 with:
/// - OSD plane: OpenGL ES rendered to GBM surface
/// - Video plane: NV12 DMA buffers with test pattern
/// </summary>
[SupportedOSPlatform("linux")]
internal class Program
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const int FrameCount = 300; // 10 seconds at 30fps

    private static readonly ILoggerFactory LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory
        .Create(builder => builder.AddConsole()
#if DEBUG
                .SetMinimumLevel(LogLevel.Trace)
#else
        .SetMinimumLevel(LogLevel.Warning)
#endif
        );

    private static readonly ILogger Logger = LoggerFactory.CreateLogger<Program>();

    static void Main(string[] args)
    {
        Logger.LogInformation("=== Multi-Plane GL Demo (DualPlanePresenter2) ===");
        Logger.LogInformation("OSD: OpenGL ES rendering to GBM surface");
        Logger.LogInformation("Video: NV12 DMA buffers with test pattern");

        System.Environment.SetEnvironmentVariable("EGL_PLATFORM", "drm");

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

        // Select planes for video (NV12) and OSD (ARGB8888)
        DualPlaneSelection planeSelection;
        try
        {
            planeSelection = DualPlaneSelector.Select(
                drmDevice,
                crtcId,
                KnownPixelFormats.DRM_FORMAT_NV12.Fourcc,     // Video plane format
                KnownPixelFormats.DRM_FORMAT_ARGB8888.Fourcc, // OSD plane format
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
            return;
        }

        // Build the presenter configuration
        var presenterConfig = DualPlanePresenterConfig.CreateBuilder()
            .WithVideoPlane(planeSelection.VideoPlane, new PlaneDrawConfiguration(Width, Height))
            .WithOsdPlane(planeSelection.OsdPlane, new PlaneDrawConfiguration(Width, Height))
            .WithCrtc(crtcId)
            .WithConnector(connector.ConnectorId)
            .WithMode(ConvertToNativeMode(mode))
            .WithZPos(planeSelection.ZPos)
            .WithLogger(Logger)
            .Build();

        // Create buffer allocator and manager for video plane
        var allocator = DmaBuffersAllocator.Create();
        var buffersManager = new DrmBufferManager(
            drmDevice,
            allocator,
            [KnownPixelFormats.DRM_FORMAT_ARGB8888, KnownPixelFormats.DRM_FORMAT_NV12],
            LoggerFactory.CreateLogger<DrmBufferManager>());

        // Create GBM device for OSD rendering
        var gbmDevice = GbmDevice.CreateFromDrmDevice(drmDevice);
        Logger.LogInformation("Created GBM device for OpenGL ES rendering");

        // Create GBM surface for OSD plane (OpenGL ES rendering)
        var gbmSurface = GbmSurfaceHelper.CreateForRendering(
            gbmDevice,
            Width,
            Height,
            KnownPixelFormats.DRM_FORMAT_ARGB8888,
            Logger);

        // Create the dual-plane presenter
        using var presenter = new DualPlanePresenter2(drmDevice, presenterConfig);
        presenter.Start();

        RunDemo(drmDevice, presenter, buffersManager, gbmDevice, gbmSurface);

        presenter.Stop();
        gbmSurface.Dispose();
        gbmDevice.Dispose();
        drmDevice.Dispose();

        Logger.LogInformation("Demo completed successfully");
    }

    private static void RunDemo(
        DrmDevice drmDevice,
        DualPlanePresenter2 presenter,
        DrmBufferManager bufferManager,
        GbmDevice gbmDevice,
        GbmSurface gbmSurface)
    {
        // Allocate buffers for video plane (NV12)
        const int videoBufferCount = 3;
        var videoBuffers = new List<SharedDmaBuffer>();
        for (int i = 0; i < videoBufferCount; i++)
        {
            var buffer = bufferManager.AllocateBuffer(Width, Height, KnownPixelFormats.DRM_FORMAT_NV12);
            buffer.MapBuffer();
            if (buffer.MapStatus == MapStatus.FailedToMap)
            {
                Logger.LogError("Failed to map video buffer {Index}", i);
                return;
            }

            // Fill with NV12 color bars test pattern
            TestPattern.FillNV12(buffer.DmaBuffer.GetMappedSpan(), Width, Height);
            buffer.DmaBuffer.SyncMap();

            videoBuffers.Add(buffer);
        }

        // Initialize OpenGL ES renderer using the GBM surface
        Logger.LogInformation("Initializing OpenGL ES renderer with GBM surface...");
        using var glRenderer = new GlSurfaceRenderer(drmDevice, gbmDevice, gbmSurface, Width, Height, Logger);
        Logger.LogInformation("OpenGL ES renderer initialized successfully!");

        Logger.LogInformation("Starting frame presentation ({FrameCount} frames)...", FrameCount);
        Logger.LogInformation("GPU renders to GBM surface -> Display scanout (DualPlanePresenter2)");

        var currentVideoIndex = 0;
        var releasedVideoBuffers = new SharedDmaBuffer[4];
        var releasedOsdBuffers = new nint[4];

        for (int frame = 0; frame < FrameCount; frame++)
        {
            // Render OpenGL ES frame to GBM surface
            glRenderer.RenderFrame(frame);

            // Lock front buffer from GBM surface after eglSwapBuffers
            var osdBo = GbmSurfaceHelper.LockFrontBuffer(gbmSurface, Logger);
            if (osdBo == 0)
            {
                Logger.LogWarning("Frame {Frame}: Failed to lock front buffer", frame);
                continue;
            }

            // Submit OSD buffer to presenter
            var (replacedOsd, releasedOsdCount) = presenter.SetOsdBuffer(osdBo, releasedOsdBuffers);

            // Release returned OSD buffers back to GBM surface
            if (replacedOsd != 0)
            {
                GbmSurfaceHelper.ReleaseBuffer(gbmSurface, replacedOsd);
            }
            for (int i = 0; i < releasedOsdCount; i++)
            {
                GbmSurfaceHelper.ReleaseBuffer(gbmSurface, releasedOsdBuffers[i]);
            }

            // Submit video frame
            var currentVideoBuffer = videoBuffers[currentVideoIndex];
            var (replacedVideo, releasedVideoCount) = presenter.EnqueueVideoFrame(
                currentVideoBuffer,
                releasedVideoBuffers);

            if (replacedVideo != null)
            {
                Logger.LogTrace("Frame {Frame}: Video buffer replaced before commit", frame);
            }

            // Cycle to next video buffer
            currentVideoIndex = (currentVideoIndex + 1) % videoBufferCount;

            // Simulate frame timing (30 fps = ~33ms per frame)
            Thread.Sleep(33);

            if (frame % 30 == 0)
            {
                Logger.LogInformation(
                    "Frame {Frame}: GPU rendered -> GBM surface -> Display scanout",
                    frame);
            }
        }

        Logger.LogInformation("Frame presentation complete!");

        // Cleanup video buffers
        foreach (var buffer in videoBuffers)
        {
            buffer.DmaBuffer.UnmapBuffer();
            buffer.Dispose();
        }

        // Drain any remaining released OSD buffers
        var finalReleasedCount = presenter.GetReleasedOsdBuffers(releasedOsdBuffers);
        for (int i = 0; i < finalReleasedCount; i++)
        {
            GbmSurfaceHelper.ReleaseBuffer(gbmSurface, releasedOsdBuffers[i]);
        }
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