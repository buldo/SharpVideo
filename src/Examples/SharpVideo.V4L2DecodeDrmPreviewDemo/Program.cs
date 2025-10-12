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

[SupportedOSPlatform("linux")]
internal class Program
{
    private const int Width = 1920;
    private const int Height = 1080;
    static async Task Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
                builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<Program>();

        logger.LogInformation("SharpVideo H.264 V4L2 Decoder with DRM Preview Demo");

        // Setup DRM display
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

        // Setup display for NV12
        var displayContext = SetupDisplay(drmDevice, Width, Height, allocator, logger);
        if (displayContext == null)
        {
            logger.LogError("Failed to setup display.");
            return;
        }

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
                RequestPoolSize = 32
            };

            int decodedFrames = 0;
            
            // Pre-allocate display buffers for double buffering
            var displayBuffer1 = AllocateAndMapDisplayBuffer(Width, Height, allocator, logger);
            var displayBuffer2 = AllocateAndMapDisplayBuffer(Width, Height, allocator, logger);
            
            if (displayBuffer1 == null || displayBuffer2 == null)
            {
                logger.LogError("Failed to allocate display buffers");
                displayBuffer1?.Dispose();
                displayBuffer2?.Dispose();
                return;
            }

            bool useBuffer1 = true;
            DmaBuffers.DmaBuffer? currentDisplayBuffer = displayBuffer1;

            await using var decoder = new H264V4L2StatelessDecoder(v4L2Device, mediaDevice, decoderLogger, config, span =>
            {
                decodedFrames++;

                // Copy decoded NV12 frame to DMA buffer and display it
                if (decodedFrames % 30 == 0) // Display every 30th frame to reduce overhead
                {
                    try
                    {
                        // Toggle between the two buffers
                        currentDisplayBuffer = useBuffer1 ? displayBuffer1 : displayBuffer2;
                        useBuffer1 = !useBuffer1;

                        // Copy frame data to the buffer
                        if (CopyFrameToExistingBuffer(span, currentDisplayBuffer, logger))
                        {
                            DisplayNv12Buffer(drmDevice, displayContext, currentDisplayBuffer, Width, Height, logger);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to display frame {FrameNumber}", decodedFrames);
                    }
                }
            });

            var decodeStopWatch = Stopwatch.StartNew();
            await decoder.DecodeStreamAsync(fileStream);

            logger.LogInformation("Decoding completed successfully in {ElapsedTime:F2} seconds!", decodeStopWatch.Elapsed.TotalSeconds);
            logger.LogInformation("Amount of decoded frames: {DecodedFrames}", decodedFrames);
            logger.LogInformation("Press Enter to exit...");
            Console.ReadLine();

            // Cleanup display buffers
            if (displayBuffer1 != null)
            {
                displayBuffer1.UnmapBuffer();
                displayBuffer1.Dispose();
            }
            if (displayBuffer2 != null)
            {
                displayBuffer2.UnmapBuffer();
                displayBuffer2.Dispose();
            }
        }
        finally
        {
            CleanupDisplay(drmDevice, displayContext, logger);
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

    private static DrmDevice? OpenDrmDevice(ILogger logger)
    {
        var devices = Directory.EnumerateFiles("/dev/dri", "card*", SearchOption.TopDirectoryOnly);
        foreach (var device in devices)
        {
            var drmDevice = DrmDevice.Open(device);
            if (drmDevice != null)
            {
                logger.LogInformation("Opened DRM device: {Device}", device);
                return drmDevice;
            }
            logger.LogWarning("Failed to open DRM device: {Device}", device);
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

        if (!SetPlane(drmDevice.DeviceFd, primaryPlane.Id, crtcId, rgbFbId, width, height, logger))
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
        int width,
        int height,
        ILogger logger)
    {
        var result = LibDrm.drmModeSetPlane(drmFd, planeId, crtcId, fbId, 0,
                                           0, 0, (uint)width, (uint)height,
                                           0, 0, (uint)width << 16, (uint)height << 16);
        if (result != 0)
        {
            logger.LogError("Failed to set plane {PlaneId}: {Result}", planeId, result);
            return false;
        }
        return true;
    }

    private static DmaBuffers.DmaBuffer? AllocateAndMapDisplayBuffer(
        int width,
        int height,
        DmaBuffersAllocator allocator,
        ILogger logger)
    {
        ulong nv12BufferSize = (ulong)(width * height * 3 / 2);
        var dmaBuf = allocator.Allocate(nv12BufferSize);
        if (dmaBuf == null)
        {
            logger.LogError("Failed to allocate NV12 DMA buffer");
            return null;
        }

        dmaBuf.MapBuffer();
        if (dmaBuf.MapStatus == MapStatus.FailedToMap)
        {
            logger.LogError("Failed to mmap NV12 DMA buffer");
            dmaBuf.Dispose();
            return null;
        }

        logger.LogDebug("Allocated display buffer with FD {Fd}", dmaBuf.Fd);
        return dmaBuf;
    }

    private static bool CopyFrameToExistingBuffer(
        ReadOnlySpan<byte> frameData,
        DmaBuffers.DmaBuffer dmaBuf,
        ILogger logger)
    {
        if (dmaBuf.MapStatus != MapStatus.Mapped)
        {
            logger.LogError("Buffer is not mapped");
            return false;
        }

        // Copy frame data to DMA buffer
        var dmaSpan = dmaBuf.GetMappedSpan();
        var copySize = Math.Min(frameData.Length, dmaSpan.Length);
        frameData.Slice(0, copySize).CopyTo(dmaSpan);

        dmaBuf.SyncMap();
        return true;
    }

    private static unsafe void DisplayNv12Buffer(
        DrmDevice drmDevice,
        DisplayContext context,
        DmaBuffers.DmaBuffer nv12Buffer,
        int width,
        int height,
        ILogger logger)
    {
        // Remove old NV12 framebuffer if exists
        if (context.Nv12FbId != 0)
        {
            LibDrm.drmModeRmFB(drmDevice.DeviceFd, context.Nv12FbId);
            context.Nv12FbId = 0;
        }

        // Create new framebuffer
        var result = LibDrm.drmPrimeFDToHandle(drmDevice.DeviceFd, nv12Buffer.Fd, out uint nv12Handle);
        if (result != 0)
        {
            logger.LogError("Failed to convert NV12 DMA FD to handle: {Result}", result);
            return;
        }

        var nv12Format = KnownPixelFormats.DRM_FORMAT_NV12.Fourcc;
        uint yPitch = (uint)width;
        uint uvPitch = (uint)width;
        uint yOffset = 0;
        uint uvOffset = (uint)(width * height);
        uint* nv12Handles = stackalloc uint[4] { nv12Handle, nv12Handle, 0, 0 };
        uint* nv12Pitches = stackalloc uint[4] { yPitch, uvPitch, 0, 0 };
        uint* nv12Offsets = stackalloc uint[4] { yOffset, uvOffset, 0, 0 };

        var resultFb = LibDrm.drmModeAddFB2(drmDevice.DeviceFd, (uint)width, (uint)height, nv12Format,
                                           nv12Handles, nv12Pitches, nv12Offsets, out var nv12FbId, 0);
        if (resultFb != 0)
        {
            logger.LogError("Failed to create NV12 framebuffer: {Result}", resultFb);
            return;
        }

        context.Nv12FbId = nv12FbId;

        // Set the plane
        if (!SetPlane(drmDevice.DeviceFd, context.Nv12Plane.Id, context.CrtcId, nv12FbId, width, height, logger))
        {
            logger.LogError("Failed to set NV12 overlay plane");
        }
    }

    private static void CleanupDisplay(DrmDevice drmDevice, DisplayContext? context, ILogger logger)
    {
        if (context == null)
            return;

        logger.LogInformation("Cleaning up display resources");

        if (context.Nv12FbId != 0)
        {
            LibDrm.drmModeRmFB(drmDevice.DeviceFd, context.Nv12FbId);
        }

        if (context.RgbFbId != 0)
        {
            LibDrm.drmModeRmFB(drmDevice.DeviceFd, context.RgbFbId);
        }

        if (context.RgbBuffer != null)
        {
            context.RgbBuffer.UnmapBuffer();
            context.RgbBuffer.Dispose();
        }
    }
}