using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;
using SharpVideo.V4L2;
using SharpVideo.V4L2StatelessDecoder.Models;
using SharpVideo.V4L2StatelessDecoder.Services;

namespace SharpVideo.V4L2DecodeDrmPreviewDemo;

/// <summary>
/// Demonstrates H.264 video decoding using V4L2 stateless decoder with zero-copy DRM display.
/// Uses DMABUF sharing between V4L2 decoder and DRM display for efficient video presentation.
/// </summary>
[SupportedOSPlatform("linux")]
internal class Program
{
    // Display resolution constants
    private const int Width = 1920;
    private const int Height = 1080;

    static async Task Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
                builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<Program>();

        logger.LogInformation("SharpVideo H.264 V4L2 Decoder with DRM Preview Demo");

        // Setup DRM display
        // Note: DrmDevice should implement IDisposable in the future for proper resource management
        var drmDevice = OpenDrmDevice(logger);
        if (drmDevice == null)
        {
            logger.LogError("No DRM devices could be opened.");
            return;
        }

        EnableDrmCapabilities(drmDevice, logger);

        if (!DmaBuffersAllocator.TryCreate(out var allocator) || allocator == null)
        {
            logger.LogError("Failed to create DMA buffers allocator.");
            return;
        }

        // Note: DmaBuffersAllocator should implement IDisposable in the future for proper resource management
        // Setup display for NV12
        var displayContext = SetupDisplay(drmDevice, Width, Height, allocator, logger);
        if (displayContext == null)
        {
            logger.LogError("Failed to setup display.");
            return;
        }

        // Create DRM buffer manager for zero-copy
        using var drmBufferManager = new DrmBufferManager(drmDevice, allocator);

        // Frame queue for async display
        var frameQueue = new System.Collections.Concurrent.ConcurrentQueue<uint>();
        var displayCts = new CancellationTokenSource();

        try
        {
            // Setup decoder
            await using var fileStream = GetFileStream();
            using var v4L2Device = GetVideoDevice(logger);
            using var mediaDevice = GetMediaDevice();

            var decoderLogger = loggerFactory.CreateLogger<H264V4L2StatelessDecoder>();
            var config = new DecoderConfiguration
            {
                OutputBufferCount = 16,
                CaptureBufferCount = 16,
                RequestPoolSize = 32,
                UseDrmPrimeBuffers = true // Enable zero-copy DMABUF mode
            };

            // Thread-safe counters for statistics
            int decodedFrames = 0;
            int presentedFrames = 0;
            int droppedFrames = 0;
            uint? lastDisplayedBufferIndex = null;
            var lockObject = new object();

            // Reference to decoder for use in callback (set after decoder creation)
            H264V4L2StatelessDecoder? decoderRef = null;

            // Fast callback - just enqueue the buffer index for async display
            Action<uint> processBuffer = (uint bufferIndex) =>
            {
                Interlocked.Increment(ref decodedFrames);

                try
                {
                    // Get DRM buffers from decoder (allocated with correct dimensions)
                    var drmBuffers = decoderRef?.DrmBuffers;
                    if (drmBuffers == null)
                    {
                        logger.LogError("DRM buffers not initialized yet");
                        return;
                    }

                    // Validate buffer index
                    if (bufferIndex >= drmBuffers.Count)
                    {
                        logger.LogError("Invalid buffer index {BufferIndex}, max is {MaxIndex}",
                            bufferIndex, drmBuffers.Count - 1);
                        return;
                    }

                    // Get the DRM buffer corresponding to this V4L2 buffer
                    var drmBuffer = drmBuffers[(int)bufferIndex];

                    // Create framebuffer if not already created (first use)
                    if (drmBuffer.FramebufferId == 0)
                    {
                        drmBufferManager.CreateFramebuffer(drmBuffer);
                        logger.LogDebug("Created framebuffer {FbId} for buffer {BufferIndex}",
                            drmBuffer.FramebufferId, bufferIndex);
                    }

                    // Enqueue for display (non-blocking)
                    frameQueue.Enqueue(bufferIndex);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error in decode callback for buffer {BufferIndex}", bufferIndex);
                    // Requeue immediately on error to avoid leaking the buffer
                    if (decoderRef != null)
                    {
                        try
                        {
                            decoderRef.RequeueCaptureBuffer(bufferIndex);
                        }
                        catch (Exception requeueEx)
                        {
                            logger.LogError(requeueEx, "Failed to requeue buffer {BufferIndex} after error", bufferIndex);
                        }
                    }
                }
            };

            // Display thread - handles presentation separately from decoding
            var displayThread = new Thread(() =>
            {
                logger.LogInformation("Display thread started");
                var displayStopwatch = Stopwatch.StartNew();
                long frameCount = 0;
                var token = displayCts.Token;

                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        // Try to get next frame to display
                        if (!frameQueue.TryDequeue(out uint bufferIndex))
                        {
                            // No frames available, wait a bit with cancellation check
                            try
                            {
                                Task.Delay(1, token).Wait();
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                            continue;
                        }

                        try
                        {
                            var drmBuffers = decoderRef?.DrmBuffers;
                            if (drmBuffers == null)
                            {
                                logger.LogWarning("DRM buffers not available in display thread");
                                continue;
                            }

                            var drmBuffer = drmBuffers[(int)bufferIndex];

                            uint? previousBufferIndex = null;
                            lock (lockObject)
                            {
                                previousBufferIndex = lastDisplayedBufferIndex;
                            }

                            // Display the buffer
                            if (SetPlane(drmDevice.DeviceFd, displayContext.Nv12Plane.Id, displayContext.CrtcId,
                                         drmBuffer.FramebufferId, drmBuffer.Width, drmBuffer.Height, Width, Height, logger))
                            {
                                lock (lockObject)
                                {
                                    presentedFrames++;
                                    lastDisplayedBufferIndex = bufferIndex;
                                }

                                frameCount++;
                                if (frameCount % 60 == 0)
                                {
                                    var elapsed = displayStopwatch.Elapsed.TotalSeconds;
                                    var displayFps = frameCount / elapsed;
                                    logger.LogDebug("Display FPS: {Fps:F2} ({FrameCount} frames)", displayFps, frameCount);
                                }

                                // Requeue the previously displayed buffer
                                if (previousBufferIndex.HasValue && decoderRef != null)
                                {
                                    decoderRef.RequeueCaptureBuffer(previousBufferIndex.Value);
                                }

                                // No artificial delay - let drmModeSetPlane naturally pace us
                                // The display system will handle the rate limiting via VSync
                            }
                            else
                            {
                                logger.LogWarning("Failed to display buffer {BufferIndex}", bufferIndex);
                                Interlocked.Increment(ref droppedFrames);
                                // Requeue immediately on failure
                                if (decoderRef != null)
                                {
                                    decoderRef.RequeueCaptureBuffer(bufferIndex);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error in display thread for buffer {BufferIndex}", bufferIndex);
                            Interlocked.Increment(ref droppedFrames);
                            // Try to requeue the buffer
                            if (decoderRef != null)
                            {
                                try
                                {
                                    decoderRef.RequeueCaptureBuffer(bufferIndex);
                                }
                                catch (Exception requeueEx)
                                {
                                    logger.LogError(requeueEx, "Failed to requeue buffer in display thread");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Fatal error in display thread");
                }

                logger.LogInformation("Display thread stopped (processed {FrameCount} frames)", frameCount);
            })
            {
                Name = "DisplayThread",
                IsBackground = false
            };

            displayThread.Start();

            await using var decoder = new H264V4L2StatelessDecoder(
                v4L2Device,
                mediaDevice,
                decoderLogger,
                config,
                processDecodedAction: null, // Not used in DMABUF mode
                processDecodedBufferIndex: processBuffer,
                drmBufferManager: drmBufferManager);

            // Set decoder reference for use in callbacks
            decoderRef = decoder;

            var decodeStopWatch = Stopwatch.StartNew();
            await decoder.DecodeStreamAsync(fileStream);
            decodeStopWatch.Stop();

            logger.LogInformation("Decoding stream completed in {ElapsedTime:F2} seconds",
                decodeStopWatch.Elapsed.TotalSeconds);
            logger.LogInformation("Decoded {FrameCount} frames, average decode FPS: {Fps:F2}",
                decodedFrames, decodedFrames / decodeStopWatch.Elapsed.TotalSeconds);

            // Wait for display thread to finish displaying queued frames
            var queueSize = frameQueue.Count;
            if (queueSize > 0)
            {
                logger.LogInformation("Waiting for display queue to drain ({Count} frames remaining)...", queueSize);
                var drainStart = Stopwatch.StartNew();
                while (!frameQueue.IsEmpty && drainStart.Elapsed.TotalSeconds < 5.0)
                {
                    await Task.Delay(100);
                }

                if (!frameQueue.IsEmpty)
                {
                    logger.LogWarning("Display queue drain timed out, {Count} frames remain", frameQueue.Count);
                }
            }

            // Stop display thread gracefully
            logger.LogInformation("Stopping display thread...");
            displayCts.Cancel();

            if (!displayThread.Join(TimeSpan.FromSeconds(3)))
            {
                logger.LogWarning("Display thread did not stop within timeout");
            }

            // Requeue the last displayed buffer
            uint? finalBufferIndex;
            lock (lockObject)
            {
                finalBufferIndex = lastDisplayedBufferIndex;
            }
            if (finalBufferIndex.HasValue)
            {
                decoder.RequeueCaptureBuffer(finalBufferIndex.Value);
                logger.LogDebug("Requeued final displayed buffer {BufferIndex}", finalBufferIndex.Value);
            }

            logger.LogInformation("=== Final Statistics (Zero-Copy Mode with Async Display) ===");
            logger.LogInformation("Total decoded frames: {DecodedFrames}", decodedFrames);
            logger.LogInformation("Frames presented on display: {PresentedFrames}", presentedFrames);
            logger.LogInformation("Frames dropped: {DroppedFrames}", droppedFrames);
            logger.LogInformation("Frame presentation rate: {Rate:F2}%",
                decodedFrames > 0 ? (presentedFrames * 100.0 / decodedFrames) : 0);
            logger.LogInformation("Average decode FPS: {Fps:F2}",
                decodedFrames / decodeStopWatch.Elapsed.TotalSeconds);
            logger.LogInformation("Average display FPS: {Fps:F2}",
                presentedFrames / decodeStopWatch.Elapsed.TotalSeconds);
            logger.LogInformation("Processing completed successfully!");
        }
        finally
        {
            displayCts.Cancel();
            CleanupDisplay(drmDevice, displayContext, logger);
            displayCts.Dispose();
        }
    }

    private static V4L2Device GetVideoDevice(ILogger logger)
    {
        var h264Devices = V4L2.V4L2DeviceManager.GetH264Devices();
        if (!h264Devices.Any())
        {
            throw new Exception("Error: No H.264 capable V4L2 devices found.");
        }

        var selectedDevice = h264Devices.First();
        logger.LogInformation("Using device: {@Device}", selectedDevice);

        var v4L2Device = V4L2DeviceFactory.Open(selectedDevice.DevicePath);
        if (v4L2Device == null)
        {
            throw new Exception($"Error: Failed to open V4L2 device at path '{selectedDevice.DevicePath}'.");
        }

        return v4L2Device;
    }

    private static MediaDevice GetMediaDevice()
    {
        // TODO: media device discovery
        var mediaDevice = MediaDevice.Open("/dev/media0");
        if (mediaDevice == null)
        {
            throw new Exception("Not able to open /dev/media0");
        }

        return mediaDevice;
    }

    private static FileStream GetFileStream()
    {
        var testVideoName = "test_video.h264";
        var filePath = File.Exists(testVideoName) ? testVideoName : Path.Combine(AppContext.BaseDirectory, testVideoName);
        if (!File.Exists(filePath))
        {
            throw new Exception(
                $"Error: Test video file '{testVideoName}' not found in current directory or application base directory.");
        }

        return File.OpenRead(filePath);
    }

    /// <summary>
    /// Opens the first available DRM device from /dev/dri/card*.
    /// </summary>
    /// <returns>Opened DRM device or null if no devices available.</returns>
    private static DrmDevice? OpenDrmDevice(ILogger logger)
    {
        var devices = Directory.EnumerateFiles("/dev/dri", "card*", SearchOption.TopDirectoryOnly);
        foreach (var device in devices)
        {
            try
            {
                var drmDevice = DrmDevice.Open(device);
                if (drmDevice != null)
                {
                    logger.LogInformation("Opened DRM device: {Device}", device);
                    return drmDevice;
                }
                logger.LogWarning("Failed to open DRM device: {Device}", device);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Exception while opening DRM device: {Device}", device);
            }
        }
        return null;
    }

    private static void EnableDrmCapabilities(DrmDevice drmDevice, ILogger logger)
    {
        var capsToEnable = new[]
        {
            DrmClientCapability.DRM_CLIENT_CAP_UNIVERSAL_PLANES,
            DrmClientCapability.DRM_CLIENT_CAP_ATOMIC
        };

        logger.LogInformation("Enabling DRM client capabilities");
        foreach (var cap in capsToEnable)
        {
            if (!drmDevice.TrySetClientCapability(cap, true, out var code))
            {
                logger.LogWarning("Failed to enable {Capability}: error {Code}", cap, code);
            }
        }
    }

    private class DisplayContext
    {
        public required uint CrtcId { get; init; }
        public required uint ConnectorId { get; init; }
        public required DrmPlane PrimaryPlane { get; init; }
        public required DrmPlane Nv12Plane { get; init; }
        public DmaBuffers.DmaBuffer? RgbBuffer { get; set; }
        public uint RgbFbId { get; set; }
        public uint Nv12FbId { get; set; }
    }

    private static DisplayContext? SetupDisplay(
        DrmDevice drmDevice,
        int width,
        int height,
        DmaBuffersAllocator allocator,
        ILogger logger)
    {
        var resources = drmDevice.GetResources();
        if (resources == null)
        {
            logger.LogError("Failed to get DRM resources");
            return null;
        }

        var connector = resources.Connectors.FirstOrDefault(c => c.Connection == DrmModeConnection.Connected);
        if (connector == null)
        {
            logger.LogError("No connected display found");
            return null;
        }
        logger.LogInformation("Found connected display: {Type}", connector.ConnectorType);

        var mode = connector.Modes.FirstOrDefault(m => m.HDisplay == width && m.VDisplay == height);
        if (mode == null)
        {
            logger.LogError("No {Width}x{Height} mode found", width, height);
            return null;
        }
        logger.LogInformation("Using mode: {Name} ({Width}x{Height}@{RefreshRate}Hz)",
            mode.Name, mode.HDisplay, mode.VDisplay, mode.VRefresh);

        var encoder = connector.Encoder ?? connector.Encoders.FirstOrDefault();
        if (encoder == null)
        {
            logger.LogError("No encoder found");
            return null;
        }

        var crtcId = encoder.CrtcId;
        if (crtcId == 0)
        {
            var availableCrtcs = resources.Crtcs
                .Where(crtc => (encoder.PossibleCrtcs & (1u << Array.IndexOf(resources.Crtcs.ToArray(), crtc))) != 0);
            crtcId = availableCrtcs.FirstOrDefault();
        }

        if (crtcId == 0)
        {
            logger.LogError("No available CRTC found");
            return null;
        }
        logger.LogInformation("Using CRTC ID: {CrtcId}", crtcId);

        var crtcIndex = resources.Crtcs.ToList().IndexOf(crtcId);
        var compatiblePlanes = resources.Planes
            .Where(p => (p.PossibleCrtcs & (1u << crtcIndex)) != 0)
            .ToList();

        var primaryPlane = compatiblePlanes.FirstOrDefault(p =>
        {
            var props = p.GetProperties();
            var typeProp = props.FirstOrDefault(prop => prop.Name.Equals("type", StringComparison.OrdinalIgnoreCase));
            return typeProp != null && typeProp.EnumNames != null &&
                   typeProp.Value < (ulong)typeProp.EnumNames.Count &&
                   typeProp.EnumNames[(int)typeProp.Value].Equals("Primary", StringComparison.OrdinalIgnoreCase);
        });

        if (primaryPlane == null)
        {
            logger.LogError("No primary plane found");
            return null;
        }
        logger.LogInformation("Found primary plane: ID {PlaneId}", primaryPlane.Id);

        var nv12Format = KnownPixelFormats.DRM_FORMAT_NV12.Fourcc;
        var nv12Plane = compatiblePlanes.FirstOrDefault(p =>
        {
            var props = p.GetProperties();
            var typeProp = props.FirstOrDefault(prop => prop.Name.Equals("type", StringComparison.OrdinalIgnoreCase));
            bool isOverlay = typeProp != null && typeProp.EnumNames != null &&
                            typeProp.Value < (ulong)typeProp.EnumNames.Count &&
                            typeProp.EnumNames[(int)typeProp.Value].Equals("Overlay", StringComparison.OrdinalIgnoreCase);
            return isOverlay && p.Formats.Contains(nv12Format);
        });

        if (nv12Plane == null)
        {
            logger.LogError("No NV12-capable overlay plane found");
            return null;
        }
        logger.LogInformation("Found NV12 overlay plane: ID {PlaneId}", nv12Plane.Id);

        // Setup RGB buffer for mode setting
        ulong rgbBufferSize = (ulong)(width * height * 4);
        var rgbBuf = allocator.Allocate(rgbBufferSize);
        if (rgbBuf == null)
        {
            logger.LogError("Failed to allocate RGB buffer");
            return null;
        }

        rgbBuf.MapBuffer();
        if (rgbBuf.MapStatus == MapStatus.FailedToMap)
        {
            logger.LogError("Failed to mmap RGB buffer");
            rgbBuf.Dispose();
            return null;
        }

        rgbBuf.GetMappedSpan().Fill(0);
        rgbBuf.SyncMap();
        logger.LogInformation("Created and filled RGB buffer");

        var (rgbFbId, _) = CreateRgbFramebuffer(drmDevice, rgbBuf, width, height, logger);
        if (rgbFbId == 0)
        {
            rgbBuf.Dispose();
            return null;
        }

        if (!SetCrtcMode(drmDevice, crtcId, connector.ConnectorId, rgbFbId, mode, width, height, logger))
        {
            LibDrm.drmModeRmFB(drmDevice.DeviceFd, rgbFbId);
            rgbBuf.Dispose();
            return null;
        }

        if (!SetPlane(drmDevice.DeviceFd, primaryPlane.Id, crtcId, rgbFbId, width, height, width, height, logger))
        {
            LibDrm.drmModeRmFB(drmDevice.DeviceFd, rgbFbId);
            rgbBuf.Dispose();
            return null;
        }

        logger.LogInformation("Display setup complete");

        return new DisplayContext
        {
            CrtcId = crtcId,
            ConnectorId = connector.ConnectorId,
            PrimaryPlane = primaryPlane,
            Nv12Plane = nv12Plane,
            RgbBuffer = rgbBuf,
            RgbFbId = rgbFbId,
            Nv12FbId = 0
        };
    }

    private static unsafe (uint fbId, uint handle) CreateRgbFramebuffer(
        DrmDevice drmDevice,
        DmaBuffers.DmaBuffer rgbBuf,
        int width,
        int height,
        ILogger logger)
    {
        var result = LibDrm.drmPrimeFDToHandle(drmDevice.DeviceFd, rgbBuf.Fd, out uint rgbHandle);
        if (result != 0)
        {
            logger.LogError("Failed to convert RGB DMA FD to handle: {Result}", result);
            return (0, 0);
        }

        var rgbFormat = KnownPixelFormats.DRM_FORMAT_XRGB8888.Fourcc;
        uint rgbPitch = (uint)(width * 4);
        uint* rgbHandles = stackalloc uint[4] { rgbHandle, 0, 0, 0 };
        uint* rgbPitches = stackalloc uint[4] { rgbPitch, 0, 0, 0 };
        uint* rgbOffsets = stackalloc uint[4] { 0, 0, 0, 0 };

        var resultFb = LibDrm.drmModeAddFB2(drmDevice.DeviceFd, (uint)width, (uint)height, rgbFormat,
                                           rgbHandles, rgbPitches, rgbOffsets, out var rgbFbId, 0);
        if (resultFb != 0)
        {
            logger.LogError("Failed to create RGB framebuffer: {Result}", resultFb);
            return (0, 0);
        }

        logger.LogInformation("Created RGB framebuffer with ID: {FbId}", rgbFbId);
        return (rgbFbId, rgbHandle);
    }

    private static unsafe bool SetCrtcMode(
        DrmDevice drmDevice,
        uint crtcId,
        uint connectorId,
        uint fbId,
        DrmModeInfo mode,
        int width,
        int height,
        ILogger logger)
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

        var nameBytes = System.Text.Encoding.UTF8.GetBytes(mode.Name);
        for (int i = 0; i < Math.Min(nameBytes.Length, 32); i++)
        {
            nativeMode.Name[i] = nameBytes[i];
        }

        var result = LibDrm.drmModeSetCrtc(drmDevice.DeviceFd, crtcId, fbId, 0, 0, &connectorId, 1, &nativeMode);
        if (result != 0)
        {
            logger.LogError("Failed to set CRTC mode: {Result}", result);
            return false;
        }

        logger.LogInformation("Successfully set CRTC to mode {Name}", mode.Name);
        return true;
    }

    private static unsafe bool SetPlane(
        int drmFd,
        uint planeId,
        uint crtcId,
        uint fbId,
        int srcWidth,
        int srcHeight,
        int dstWidth,
        int dstHeight,
        ILogger logger)
    {
        // srcWidth/srcHeight: dimensions of the framebuffer (may be padded, e.g., 1920x1088)
        // dstWidth/dstHeight: dimensions to display on screen (e.g., 1920x1080)
        var result = LibDrm.drmModeSetPlane(drmFd, planeId, crtcId, fbId, 0,
                                           0, 0, (uint)dstWidth, (uint)dstHeight,
                                           0, 0, (uint)srcWidth << 16, (uint)srcHeight << 16);
        if (result != 0)
        {
            logger.LogError("Failed to set plane {PlaneId}: {Result}", planeId, result);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Cleans up display resources including framebuffers and DMA buffers.
    /// </summary>
    /// <summary>
    /// Cleans up display resources including framebuffers and DMA buffers.
    /// </summary>
    private static void CleanupDisplay(DrmDevice drmDevice, DisplayContext? context, ILogger logger)
    {
        if (context == null)
            return;

        logger.LogInformation("Cleaning up display resources");

        try
        {
            if (context.Nv12FbId != 0)
            {
                var result = LibDrm.drmModeRmFB(drmDevice.DeviceFd, context.Nv12FbId);
                if (result != 0)
                {
                    logger.LogWarning("Failed to remove NV12 framebuffer {FbId}: {Result}",
                        context.Nv12FbId, result);
                }
            }

            if (context.RgbFbId != 0)
            {
                var result = LibDrm.drmModeRmFB(drmDevice.DeviceFd, context.RgbFbId);
                if (result != 0)
                {
                    logger.LogWarning("Failed to remove RGB framebuffer {FbId}: {Result}",
                        context.RgbFbId, result);
                }
            }

            if (context.RgbBuffer != null)
            {
                context.RgbBuffer.UnmapBuffer();
                context.RgbBuffer.Dispose();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during display cleanup");
        }
    }
}