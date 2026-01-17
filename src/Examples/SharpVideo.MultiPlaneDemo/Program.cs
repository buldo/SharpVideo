using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using Microsoft.Extensions.Logging;

using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.C;
using SharpVideo.Linux.Native.Gbm;
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
            Logger.LogInformation("=== Multi-Plane Compositing Demo (DualPlanePresenter2) ===");
            Logger.LogInformation("This demo shows overlay of OSD (ARGB8888) and Video (NV12) planes");
            Logger.LogInformation("OSD plane will be displayed ON TOP of video plane using zpos");

            var drmDevice = DrmUtils.OpenDrmDevice(Logger);
            drmDevice.EnableDrmCapabilities(Logger);

            // Get device resources to find connector, CRTC, and mode
            var resources = drmDevice.GetResources();
            if (resources == null)
            {
                Logger.LogError("Failed to get DRM resources");
                return;
            }

            // Find the first connected connector
            var connector = resources.Connectors
                .FirstOrDefault(c => c.Connection == DrmModeConnection.Connected);

            if (connector == null)
            {
                Logger.LogError("No connected display found");
                return;
            }

            Logger.LogInformation("Found connector: {Type}-{TypeId} (ID: {Id})",
                connector.ConnectorType, connector.ConnectorTypeId, connector.ConnectorId);

            // Get the preferred mode (or first mode matching Width x Height)
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

            // Get CRTC ID from the connector's current encoder
            uint crtcId = connector.Encoder?.CrtcId ?? 0;
            if (crtcId == 0)
            {
                // Fall back to first CRTC
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

            // Create buffer allocator and manager
            var allocator = DmaBuffersAllocator.Create();
            var buffersManager = new DrmBufferManager(
                drmDevice,
                allocator,
                [KnownPixelFormats.DRM_FORMAT_ARGB8888, KnownPixelFormats.DRM_FORMAT_NV12],
                LoggerFactory.CreateLogger<DrmBufferManager>());

            // Create GBM device for OSD buffer allocation
            var gbmDevice = LibGbm.CreateDevice(drmDevice.DeviceFd);
            if (gbmDevice == 0)
            {
                Logger.LogError("Failed to create GBM device");
                return;
            }

            // Create the dual-plane presenter
            using var presenter = new DualPlanePresenter2(drmDevice, presenterConfig);
            presenter.Start();

            RunDemo(presenter, buffersManager, gbmDevice);

            presenter.Stop();
            LibGbm.DestroyDevice(gbmDevice);
            drmDevice.Dispose();

            Logger.LogInformation("Demo completed successfully");
        }

        private static void RunDemo(DualPlanePresenter2 presenter, DrmBufferManager bufferManager, nint gbmDevice)
        {
            // Allocate buffers for video plane (NV12) - we need 3 buffers for the pipeline
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

            // Allocate buffer for OSD plane (ARGB8888) using GBM
            // Need SCANOUT for display + LINEAR for CPU mapping
            var osdBo = LibGbm.CreateBo(
                gbmDevice,
                Width, Height,
                KnownPixelFormats.DRM_FORMAT_ARGB8888.Fourcc,
                GbmBoUse.GBM_BO_USE_SCANOUT | GbmBoUse.GBM_BO_USE_LINEAR);

            if (osdBo == 0)
            {
                // Fallback: try without LINEAR (some drivers don't support it)
                Logger.LogWarning("Failed to create GBM BO with SCANOUT | LINEAR, trying SCANOUT only");
                osdBo = LibGbm.CreateBo(
                    gbmDevice,
                    Width, Height,
                    KnownPixelFormats.DRM_FORMAT_ARGB8888.Fourcc,
                    GbmBoUse.GBM_BO_USE_SCANOUT);
            }

            if (osdBo == 0)
            {
                Logger.LogError("Failed to create GBM buffer object for OSD");
                return;
            }

            Logger.LogInformation("Created GBM BO for OSD: {Bo}", osdBo);

            // Map and fill OSD with semi-transparent test pattern
            FillOsdGbmBuffer(osdBo, Width, Height);

            Logger.LogInformation("Starting frame presentation ({FrameCount} frames)...", FrameCount);

            // Set OSD buffer once at the start
            presenter.SetOsdBuffer(osdBo);

            var currentVideoIndex = 0;
            var releasedBuffers = new SharedDmaBuffer[1];

            for (int frame = 0; frame < FrameCount; frame++)
            {
                // Get a video buffer to submit
                var currentVideoBuffer = videoBuffers[currentVideoIndex];

                // Enqueue video frame - returns previously pending buffer if any
                var replacedBuffer = presenter.EnqueueVideoFrame(currentVideoBuffer);
                if (replacedBuffer != null)
                {
                    // Buffer was replaced before being committed, can reuse immediately
                    Logger.LogTrace("Frame {Frame}: Buffer replaced before commit", frame);
                }

                // Check for released video buffers (finished displaying)
                var releasedCount = presenter.GetReleasedVideoBuffers(releasedBuffers);
                if (releasedCount > 0)
                {
                    Logger.LogTrace("Frame {Frame}: Released {Count} video buffer(s)", frame, releasedCount);
                }

                // Cycle to next buffer
                currentVideoIndex = (currentVideoIndex + 1) % videoBufferCount;

                // Simulate frame timing (30 fps = ~33ms per frame)
                Thread.Sleep(33);

                if (frame % 30 == 0)
                {
                    Logger.LogInformation("Presented {Frame} frames", frame);
                }
            }

            Logger.LogInformation("Frame presentation complete");

            // Cleanup video buffers
            foreach (var buffer in videoBuffers)
            {
                buffer.DmaBuffer.UnmapBuffer();
                buffer.Dispose();
            }

            // Cleanup OSD GBM buffer
            LibGbm.DestroyBo(osdBo);
        }

        /// <summary>
        /// Fills OSD GBM buffer with a semi-transparent test pattern.
        /// </summary>
        private static unsafe void FillOsdGbmBuffer(nint bo, int width, int height)
        {
            uint stride;
            void* mapData = null;

            // Use gbm_bo_map for proper CPU access
            var ptr = LibGbm.Map(
                bo,
                0, 0,
                (uint)width, (uint)height,
                LibGbm.GbmBoTransferFlags.GBM_BO_TRANSFER_WRITE,
                &stride,
                &mapData);

            if (ptr == null)
            {
                Logger.LogError("Failed to map GBM buffer via gbm_bo_map");
                return;
            }

            try
            {
                var size = stride * (uint)height;
                var span = new Span<byte>(ptr, (int)size);

                // Fill with semi-transparent test pattern (ARGB8888)
                // Create a gradient with transparency
                for (int y = 0; y < height; y++)
                {
                    var rowOffset = y * (int)stride;
                    for (int x = 0; x < width; x++)
                    {
                        var pixelOffset = rowOffset + x * 4;
                        // Semi-transparent red-to-blue gradient
                        byte alpha = (byte)(128 + (y * 127 / height)); // 128-255 alpha
                        byte red = (byte)(255 - x * 255 / width);
                        byte green = 64;
                        byte blue = (byte)(x * 255 / width);

                        span[pixelOffset + 0] = blue;  // B
                        span[pixelOffset + 1] = green; // G
                        span[pixelOffset + 2] = red;   // R
                        span[pixelOffset + 3] = alpha; // A
                    }
                }

                Logger.LogDebug("OSD GBM buffer filled with test pattern, stride={Stride}", stride);
            }
            finally
            {
                LibGbm.Unmap(bo, mapData);
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
}
