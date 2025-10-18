using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;
using SharpVideo.Utils;
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
        var drmDevice = DrmUtils.OpenDrmDevice(logger);
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

        using var drmBufferManager = new DrmBufferManager(drmDevice, allocator, [KnownPixelFormats.DRM_FORMAT_NV12, KnownPixelFormats.DRM_FORMAT_XRGB8888]);
        var presenter = DrmPresenter.Create(drmDevice, Width, Height, drmBufferManager, logger);

        // Latest frame tracking for minimal latency display
        // Instead of queue, we always display the most recent decoded frame
        uint? latestFrameIndex = null;
        bool hasNewFrame = false;
        var frameLock = new object();
        var displayCts = new CancellationTokenSource();

        try
        {
            // Setup decoder
            await using var fileStream = GetFileStream();
            var (v4L2Device, deviceInfo) = GetVideoDevice(logger);
            using var _ = v4L2Device; // Ensure disposal
            using var mediaDevice = GetMediaDevice();

            var decoderLogger = loggerFactory.CreateLogger<H264V4L2StatelessDecoder>();

            // Configure decoder based on device capabilities for optimal low-latency performance
            var config = new DecoderConfiguration
            {
                // Use more buffers if streaming is supported for smoother playback
                OutputBufferCount = deviceInfo.DeviceCapabilities.HasFlag(V4L2Capabilities.STREAMING) ? 16u : 8u,
                CaptureBufferCount = deviceInfo.DeviceCapabilities.HasFlag(V4L2Capabilities.STREAMING) ? 16u : 8u,
                RequestPoolSize = 32,
                UseDrmPrimeBuffers = true // Enable zero-copy DMABUF mode for lowest latency
            };

            logger.LogInformation("Decoder configuration: OutputBuffers={Output}, CaptureBuffers={Capture}, " +
                                "RequestPool={Pool}, DrmPrime={Drm}",
                config.OutputBufferCount, config.CaptureBufferCount,
                config.RequestPoolSize, config.UseDrmPrimeBuffers);
            logger.LogInformation("Display strategy: Latest-frame mode (minimal latency, skips intermediate frames)");

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

                    // Store as latest frame (replaces previous if not yet displayed)
                    lock (frameLock)
                    {
                        // If there was a previous undisplayed frame, requeue it immediately
                        if (hasNewFrame && latestFrameIndex.HasValue && latestFrameIndex.Value != bufferIndex)
                        {
                            decoderRef?.RequeueCaptureBuffer(latestFrameIndex.Value);
                        }
                        latestFrameIndex = bufferIndex;
                        hasNewFrame = true;
                    }
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
            var displayStopwatch = new Stopwatch();
            var setPlaneTimings = new List<double>();
            var displayThread = new Thread(() =>
            {
                logger.LogInformation("Display thread started");
                displayStopwatch.Start();
                long frameCount = 0;
                var token = displayCts.Token;

                // Use high-precision timing if monotonic timestamps are available
                var useMonotonicTiming = presenter.TimestampMonotonic;
                var lastFrameTime = displayStopwatch.Elapsed.TotalSeconds;
                double minFrameTime = double.MaxValue;
                double maxFrameTime = 0;

                try
                {
                    var loopIterationTimer = Stopwatch.StartNew();
                    while (!token.IsCancellationRequested)
                    {
                        // Get latest frame to display
                        uint bufferIndex;
                        bool frameAvailable;

                        lock (frameLock)
                        {
                            frameAvailable = hasNewFrame && latestFrameIndex.HasValue;
                            if (frameAvailable)
                            {
                                bufferIndex = latestFrameIndex!.Value;
                                hasNewFrame = false; // Mark as consumed
                            }
                            else
                            {
                                bufferIndex = 0; // Will not be used
                            }
                        }

                        if (!frameAvailable)
                        {
                            // No new frames available, use adaptive spinning for efficiency
                            // SpinWait provides hybrid spinning with automatic backoff
                            if (token.IsCancellationRequested)
                            {
                                break;
                            }
                            Thread.SpinWait(100); // Brief spin (microseconds range)
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

                            // Display the buffer - measure actual display latency
                            var setPlaneStart = Stopwatch.GetTimestamp();
                            var setPlaneSuccess = presenter.SetOverlayPlane(drmBuffer, Width, Height);
                            var setPlaneElapsed = (Stopwatch.GetTimestamp() - setPlaneStart) * 1000.0 / Stopwatch.Frequency;

                            if (setPlaneSuccess)
                            {
                                lock (lockObject)
                                {
                                    presentedFrames++;
                                    lastDisplayedBufferIndex = bufferIndex;
                                }

                                frameCount++;

                                // Log timing for performance analysis
                                if (frameCount <= 10 || frameCount % 10 == 0)
                                {
                                    var totalLoopTime = loopIterationTimer.Elapsed.TotalMilliseconds;
                                    logger.LogInformation("Frame {Count}: SetPlane={SetPlaneMs:F2}ms, TotalLoop={TotalMs:F2}ms, Overhead={OverheadMs:F2}ms",
                                        frameCount, setPlaneElapsed, totalLoopTime, totalLoopTime - setPlaneElapsed);
                                    loopIterationTimer.Restart();
                                }

                                // Track frame timing for detailed statistics
                                if (useMonotonicTiming && frameCount > 1)
                                {
                                    var currentTime = displayStopwatch.Elapsed.TotalSeconds;
                                    var frameTime = (currentTime - lastFrameTime) * 1000.0; // Convert to ms
                                    minFrameTime = Math.Min(minFrameTime, frameTime);
                                    maxFrameTime = Math.Max(maxFrameTime, frameTime);
                                    lastFrameTime = currentTime;
                                }

                                if (frameCount % 60 == 0)
                                {
                                    var elapsed = displayStopwatch.Elapsed.TotalSeconds;
                                    var displayFps = frameCount / elapsed;
                                    if (useMonotonicTiming && frameCount > 1)
                                    {
                                        logger.LogDebug("Display: {Fps:F2} FPS avg, frame time: {Min:F2}-{Max:F2}ms ({FrameCount} frames)",
                                            displayFps, minFrameTime, maxFrameTime, frameCount);
                                        // Reset min/max for next interval
                                        minFrameTime = double.MaxValue;
                                        maxFrameTime = 0;
                                    }
                                    else
                                    {
                                        logger.LogDebug("Display FPS: {Fps:F2} ({FrameCount} frames)", displayFps, frameCount);
                                    }
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
                finally
                {
                    displayStopwatch.Stop();
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

            // Give display thread a moment to show the last frame (2 frames at 60 FPS)
            logger.LogInformation("Waiting for final frame display...");
            await Task.Delay(33);

            // Check if there's still an undisplayed frame
            lock (frameLock)
            {
                if (hasNewFrame && latestFrameIndex.HasValue)
                {
                    logger.LogInformation("Last decoded frame not displayed yet");
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

            logger.LogInformation("=== Final Statistics (Latest-Frame Low-Latency Mode) ===");
            logger.LogInformation("Total decoded frames: {DecodedFrames}", decodedFrames);
            logger.LogInformation("Frames presented on display: {PresentedFrames}", presentedFrames);
            logger.LogInformation("Frames skipped (not displayed): {SkippedFrames}", decodedFrames - presentedFrames - droppedFrames);
            logger.LogInformation("Frames dropped (errors): {DroppedFrames}", droppedFrames);
            logger.LogInformation("Frame presentation rate: {Rate:F2}%",
                decodedFrames > 0 ? (presentedFrames * 100.0 / decodedFrames) : 0);
            logger.LogInformation("Average decode FPS: {Fps:F2}",
                decodedFrames / decodeStopWatch.Elapsed.TotalSeconds);
            logger.LogInformation("Average display FPS: {Fps:F2} (measured over {Duration:F2}s)",
                displayStopwatch.Elapsed.TotalSeconds > 0 ? presentedFrames / displayStopwatch.Elapsed.TotalSeconds : 0,
                displayStopwatch.Elapsed.TotalSeconds);
            logger.LogInformation("Decode duration: {Duration:F2}s, Display duration: {DisplayDuration:F2}s",
                decodeStopWatch.Elapsed.TotalSeconds, displayStopwatch.Elapsed.TotalSeconds);
            logger.LogInformation("Latency: Minimal (always displaying latest decoded frame)");
            logger.LogInformation("Processing completed successfully!");
        }
        finally
        {
            displayCts.Cancel();
            presenter.CleanupDisplay();
            displayCts.Dispose();
        }
    }

    private static (V4L2Device device, V4L2DeviceInfo deviceInfo) GetVideoDevice(ILogger logger)
    {
        var h264Devices = V4L2.V4L2DeviceManager.GetH264Devices();
        if (!h264Devices.Any())
        {
            throw new Exception("Error: No H.264 capable V4L2 devices found.");
        }

        var selectedDevice = h264Devices.First();
        logger.LogInformation("Using device: {@Device}", selectedDevice);

        // Log device capabilities for optimization analysis
        LogDeviceCapabilities(selectedDevice, logger);

        var v4L2Device = V4L2DeviceFactory.Open(selectedDevice.DevicePath);
        if (v4L2Device == null)
        {
            throw new Exception($"Error: Failed to open V4L2 device at path '{selectedDevice.DevicePath}'.");
        }

        return (v4L2Device, selectedDevice);
    }

    private static void LogDeviceCapabilities(V4L2DeviceInfo deviceInfo, ILogger logger)
    {
        logger.LogInformation("Driver: {Driver}; Card: {Card}; Device Path: {Path}; DeviceCapabilities: {DeviceCapabilities}", deviceInfo.DriverName, deviceInfo.CardName, deviceInfo.DevicePath, deviceInfo.DeviceCapabilities);

        logger.LogInformation("=== Supported Formats ===");
        foreach (var format in deviceInfo.SupportedFormats)
        {
            logger.LogInformation("  Format: {Description} (FourCC: {FourCC})",
                format.Description, format.PixelFormat);
        }
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
}