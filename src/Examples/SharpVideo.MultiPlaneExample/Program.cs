using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Utils;

namespace SharpVideo.MultiPlaneExample
{
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
            Logger.LogInformation("=== Multi-Plane Compositing Demo ===");
            Logger.LogInformation("This demo shows overlay of Primary (ARGB8888) and Overlay (NV12) planes");
            Logger.LogInformation("Primary plane will be displayed ON TOP of overlay plane using zpos");

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
                KnownPixelFormats.DRM_FORMAT_ARGB8888,  // Primary plane format
                KnownPixelFormats.DRM_FORMAT_NV12,  // Overlay plane format
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
            var primaryZposRange = presenter.PrimaryPlane.GetPlaneZPositionRange();
            var overlayZposRange = presenter.OverlayPlane.GetPlaneZPositionRange();

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
            // Use the actual supported range values
            if (primaryZposRange.HasValue && overlayZposRange.HasValue)
            {
                // Set primary to max, overlay to min to ensure proper layering
                var primaryZpos = primaryZposRange.Value.max;
                var overlayZpos = overlayZposRange.Value.min;

                Logger.LogInformation("Attempting to set Primary zpos={PrimaryZpos}, Overlay zpos={OverlayZpos}",
                    primaryZpos, overlayZpos);

                var primarySuccess = presenter.PrimaryPlane.SetPlaneZPosition(primaryZpos);
                var overlaySuccess = presenter.OverlayPlane.SetPlaneZPosition(overlayZpos);

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

            // Fill primary plane with ARGB8888 test pattern (semi-transparent)
            var primaryBuffer = presenter.PrimaryPlanePresenter.GetPrimaryPlaneBackBuffer();
            TestPattern.FillARGB8888(primaryBuffer, Width, Height);

            // Present the primary plane
            if (!presenter.PrimaryPlanePresenter.SwapPrimaryPlaneBuffers())
            {
                Logger.LogError("Failed to present primary plane");
                return;
            }

            Logger.LogInformation("Starting frame presentation ({FrameCount} frames)...", FrameCount);

            var currentOverlayIndex = 0;

            for (int frame = 0; frame < FrameCount; frame++)
            {
                // Update overlay plane - cycle through buffers to demonstrate buffer management
                var currentBuffer = overlayBuffers[currentOverlayIndex];
                presenter.OverlayPlanePresenter.SetOverlayPlaneBuffer(currentBuffer);

                // Get completed buffers
                var completed = presenter.OverlayPlanePresenter.GetPresentedOverlayBuffers();

                // Simulate frame timing (30 fps = ~33ms per frame)
                Thread.Sleep(33);

                // Cycle to next buffer
                currentOverlayIndex = (currentOverlayIndex + 1) % overlayBufferCount;

                if (frame % 30 == 0)
                {
                    Logger.LogInformation("Presented {Frame} frames, completed buffers: {Count}",
                        frame, completed.Length);
                }
            }

            Logger.LogInformation("Frame presentation complete");

            // Cleanup overlay buffers
            foreach (var buffer in overlayBuffers)
            {
                buffer.DmaBuffer.UnmapBuffer();
                buffer.Dispose();
            }
        }
    }
}
