using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Utils;

namespace SharpVideo.MultiPlaneGlExample;

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
        System.Environment.SetEnvironmentVariable("EGL_PLATFORM", "drm");

        var drmDevice = DrmUtils.OpenDrmDevice(Logger);
        drmDevice.EnableDrmCapabilities(Logger);

        var allocator = DmaBuffersAllocator.Create();
        var buffersManager = new DrmBufferManager(
            drmDevice,
            allocator,
            [KnownPixelFormats.DRM_FORMAT_ARGB8888, KnownPixelFormats.DRM_FORMAT_NV12],
            LoggerFactory.CreateLogger<DrmBufferManager>());

        var presenter = DrmPresenter.Create(
            drmDevice,
            Width,
            Height,
            buffersManager,
            KnownPixelFormats.DRM_FORMAT_ARGB8888, // Primary plane format
            KnownPixelFormats.DRM_FORMAT_NV12, // Overlay plane format
            Logger);

        if (presenter == null)
        {
            Logger.LogError("Failed to create presenter");
            return;
        }

        try
        {
            RunDemo(presenter, buffersManager);
        }
        finally
        {
            presenter.CleanupDisplay();
            drmDevice.Dispose();
        }

        Logger.LogInformation("Demo completed successfully");
    }

    private static void RunDemo(DrmPresenter presenter, DrmBufferManager bufferManager)
    {
        // Get zpos ranges for both planes
        var primaryZposRange = presenter.GetPlaneZPositionRange(presenter.GetPrimaryPlaneId());
        var overlayZposRange = presenter.GetPlaneZPositionRange(presenter.GetOverlayPlaneId());

        if (primaryZposRange.HasValue)
        {
            Logger.LogInformation("Primary plane zpos range: [{Min}, {Max}], current: {Current}",
                primaryZposRange.Value.min, primaryZposRange.Value.max, primaryZposRange.Value.current);
        }
        else
        {
            Logger.LogWarning("Primary plane does not support zpos property");
        }

        if (overlayZposRange.HasValue)
        {
            Logger.LogInformation("Overlay plane zpos range: [{Min}, {Max}], current: {Current}",
                overlayZposRange.Value.min, overlayZposRange.Value.max, overlayZposRange.Value.current);
        }
        else
        {
            Logger.LogWarning("Overlay plane does not support zpos property");
        }

        // Try to set z-position to make primary plane appear on top
        if (primaryZposRange.HasValue && overlayZposRange.HasValue)
        {
            var primaryZpos = primaryZposRange.Value.max;
            var overlayZpos = overlayZposRange.Value.min;

            Logger.LogInformation("Attempting to set Primary zpos={PrimaryZpos}, Overlay zpos={OverlayZpos}",
                primaryZpos, overlayZpos);

            var primarySuccess = presenter.SetPlaneZPosition(presenter.GetPrimaryPlaneId(), primaryZpos);
            var overlaySuccess = presenter.SetPlaneZPosition(presenter.GetOverlayPlaneId(), overlayZpos);

            if (primarySuccess && overlaySuccess)
            {
                Logger.LogInformation("Z-positioning successful: Primary on top, Overlay below");
            }
            else
            {
                Logger.LogWarning("Failed to set z-positions as desired");
            }
        }

        // Allocate buffers for overlay plane (NV12)
        var overlayBufferCount = 3;
        var overlayBuffers = new List<SharedDmaBuffer>();
        for (int i = 0; i < overlayBufferCount; i++)
        {
            var buffer = bufferManager.AllocateBuffer(Width, Height, KnownPixelFormats.DRM_FORMAT_NV12);
            buffer.MapBuffer();
            if (buffer.MapStatus == MapStatus.FailedToMap)
            {
                Logger.LogError("Failed to map overlay buffer {Index}", i);
                return;
            }

            // Fill with NV12 color bars test pattern
            TestPattern.FillNV12(buffer.DmaBuffer.GetMappedSpan(), Width, Height);
            buffer.DmaBuffer.SyncMap();

            overlayBuffers.Add(buffer);
        }

        // Initialize hardware-accelerated OpenGL ES renderer with DMA-BUF support
        Logger.LogInformation("Initializing hardware-accelerated OpenGL ES renderer...");
        Logger.LogInformation("Using EGL + DMA-BUF extensions for zero-copy rendering");
        using var glRenderer = new GlRenderer(Width, Height, Logger);
        Logger.LogInformation("OpenGL ES renderer initialized successfully!");
        Logger.LogInformation("");

        // Present initial overlay buffer
        presenter.SetOverlayPlaneBuffer(overlayBuffers[0]);

        Logger.LogInformation("Starting frame presentation ({FrameCount} frames)...", FrameCount);
        Logger.LogInformation("GPU renders directly to DMA-BUF -> Display hardware scans out -> ZERO COPIES!");
        Logger.LogInformation("");

        var currentOverlayIndex = 0;

        for (int frame = 0; frame < FrameCount; frame++)
        {
            // Get the current back buffer DMA-BUF for GPU rendering
            var primaryDmaBuffer = presenter.GetPrimaryPlaneBackBufferDma();

// Render OpenGL ES content directly to the DMA buffer (ZERO-COPY!)
            // The GPU writes directly to the buffer that the display hardware will scan out
            glRenderer.RenderToDmaBuffer(primaryDmaBuffer);

            // Present the primary plane (swap buffers)
            // This just tells the display hardware to switch to the newly rendered buffer
            if (!presenter.SwapPrimaryPlaneBuffers())
            {
                Logger.LogError("Failed to present primary plane at frame {Frame}", frame);
                break;
            }

            // Update overlay plane - cycle through buffers
            var currentBuffer = overlayBuffers[currentOverlayIndex];
            presenter.SetOverlayPlaneBuffer(currentBuffer);

// Get completed buffers
            var completed = presenter.GetPresentedOverlayBuffers();

            // Simulate frame timing (30 fps = ~33ms per frame)
            Thread.Sleep(33);

            // Cycle to next buffer
            currentOverlayIndex = (currentOverlayIndex + 1) % overlayBufferCount;

            if (frame % 30 == 0)
            {
                Logger.LogInformation(
                    "Frame {Frame}: GPU rendered -> DMA-BUF -> Display scanout (zero-copy!), completed: {Count}",
                    frame, completed.Length);
            }
        }

        Logger.LogInformation("");
        Logger.LogInformation("Frame presentation complete!");
        Logger.LogInformation("All {FrameCount} frames were rendered by GPU directly to DMA-BUF with ZERO copies!",
            FrameCount);

        // Cleanup overlay buffers
        foreach (var buffer in overlayBuffers)
        {
            buffer.DmaBuffer.UnmapBuffer();
            buffer.Dispose();
        }
    }
}