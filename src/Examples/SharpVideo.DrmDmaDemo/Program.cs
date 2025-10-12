using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;
using System.Runtime.Versioning;

namespace SharpVideo.DrmDmaDemo
{
    [SupportedOSPlatform("linux")]
    internal class Program
    {
        private const int Width = 1920;
        private const int Height = 1080;

        static void Main(string[] args)
        {
            var drmDevice = OpenDrmDevice();
            if (drmDevice == null)
            {
                Console.WriteLine("No DRM devices could be opened.");
                return;
            }

            EnableDrmCapabilities(drmDevice);
            DumpDeviceCaps(drmDevice);

            if (!DmaBuffersAllocator.TryCreate(out var allocator) || allocator == null)
            {
                Console.WriteLine("Failed to create DMA buffers allocator.");
                return;
            }

            var nv12Buffer = CreateAndFillNv12Buffer(allocator, Width, Height);
            if (nv12Buffer == null)
            {
                return;
            }

            try
            {
                if (PresentNv12Buffer(drmDevice, nv12Buffer, Width, Height, allocator))
                {
                    Console.WriteLine("Successfully presented buffer on display.");
                    Console.WriteLine("Displaying pattern for 10 seconds...");
                    Thread.Sleep(10000);
                }
                else
                {
                    Console.WriteLine("Failed to present buffer on display.");
                }
            }
            finally
            {
                nv12Buffer.UnmapBuffer();
                Console.WriteLine("Unmapped DMA buffer.");
                nv12Buffer.Dispose();
            }
        }

        private static DrmDevice? OpenDrmDevice()
        {
            var devices = Directory.EnumerateFiles("/dev/dri", "card*", SearchOption.TopDirectoryOnly);
            foreach (var device in devices)
            {
                var drmDevice = DrmDevice.Open(device);
                if (drmDevice != null)
                {
                    Console.WriteLine($"Opened DRM device: {device}");
                    return drmDevice;
                }
                Console.WriteLine($"Failed to open DRM device: {device}");
            }
            return null;
        }

        private static void EnableDrmCapabilities(DrmDevice drmDevice)
        {
            var capsToEnable = new[]
            {
                DrmClientCapability.DRM_CLIENT_CAP_UNIVERSAL_PLANES,
                DrmClientCapability.DRM_CLIENT_CAP_ATOMIC
            };

            Console.WriteLine("Enabling DRM client capabilities:");
            foreach (var cap in capsToEnable)
            {
                if (!drmDevice.TrySetClientCapability(cap, true, out var code))
                {
                    Console.WriteLine($"  {cap}: Failed (error {code})");
                }
                else
                {
                    Console.WriteLine($"  {cap}: Enabled");
                }
            }
        }

        private static DmaBuffers.DmaBuffer? CreateAndFillNv12Buffer(DmaBuffersAllocator allocator, int width, int height)
        {
            ulong nv12BufferSize = (ulong)(width * height * 3.0 / 2.0);
            var dmaBuf = allocator.Allocate(nv12BufferSize);
            if (dmaBuf == null)
            {
                Console.WriteLine("Failed to allocate NV12 DMA buffer.");
                return null;
            }

            Console.WriteLine($"Allocated NV12 buffer of size {dmaBuf.Size} with fd {dmaBuf.Fd}");

            dmaBuf.MapBuffer();
            if (dmaBuf.MapStatus == MapStatus.FailedToMap)
            {
                Console.WriteLine("Failed to mmap DMA buffer.");
                dmaBuf.Dispose();
                return null;
            }

            Console.WriteLine("DMA buffer mapped");
            TestPattern.FillNV12(dmaBuf.GetMappedSpan(), width, height);
            Console.WriteLine("Filled DMA buffer with NV12 test pattern.");

            dmaBuf.SyncMap();
            Console.WriteLine("Synced DMA buffer.");

            var roResult = dmaBuf.MakeMapReadOnly();
            Console.WriteLine(roResult
                ? "Made buffer read-only to prevent modification."
                : "Warning: Failed to make buffer read-only, continuing anyway.");

            return dmaBuf;
        }

        private static void DumpDeviceCaps(DrmDevice drmDevice)
        {
            Console.WriteLine("### Device caps ###");
            var caps = drmDevice.GetDeviceCapabilities();
            Console.WriteLine($"DRM_CAP_DUMB_BUFFER = {caps.DumbBuffer}");
            Console.WriteLine($"DRM_CAP_VBLANK_HIGH_CRTC = {caps.VblankHighCrtc}");
            Console.WriteLine($"DRM_CAP_DUMB_PREFERRED_DEPTH = {caps.DumbPreferredDepth}");
            Console.WriteLine($"DRM_CAP_DUMB_PREFER_SHADOW = {caps.DumbPreferShadow}");
            Console.WriteLine($"DRM_CAP_PRIME = {caps.Prime}");
            Console.WriteLine($"DRM_CAP_TIMESTAMP_MONOTONIC = {caps.TimestampMonotonic}");
            Console.WriteLine($"DRM_CAP_ASYNC_PAGE_FLIP = {caps.AsyncPageFlip}");
            Console.WriteLine($"DRM_CAP_CURSOR_WIDTH = {caps.CursorWidth}");
            Console.WriteLine($"DRM_CAP_CURSOR_HEIGHT = {caps.CursorWidth}");
            Console.WriteLine($"DRM_CAP_ADDFB2_MODIFIERS = {caps.AddFB2Modifiers}");
            Console.WriteLine($"DRM_CAP_PAGE_FLIP_TARGET = {caps.PageFlipTarget}");
            Console.WriteLine($"DRM_CAP_CRTC_IN_VBLANK_EVENT = {caps.CrtcInVblankEvent}");
            Console.WriteLine($"DRM_CAP_SYNCOBJ = {caps.SyncObj}");
            Console.WriteLine($"DRM_CAP_SYNCOBJ_TIMELINE = {caps.SyncObjTimeline}");
            Console.WriteLine($"DRM_CAP_ATOMIC_ASYNC_PAGE_FLIP = {caps.AtomicAsyncPageFlip}");
        }

        private static DrmConnector? FindConnectedConnector(DrmDeviceResources resources)
        {
            var connector = resources.Connectors.FirstOrDefault(c => c.Connection == DrmModeConnection.Connected);
            if (connector == null)
            {
                Console.WriteLine("No connected display found");
                return null;
            }

            Console.WriteLine($"Found connected display: {connector.ConnectorType}");
            return connector;
        }

        private static DrmModeInfo? FindDisplayMode(DrmConnector connector, int width, int height)
        {
            Console.WriteLine($"Available modes ({connector.Modes.Count}):");
            foreach (var m in connector.Modes)
            {
                Console.WriteLine($"  {m.Name}: {m.HDisplay}x{m.VDisplay}@{m.VRefresh}Hz, Type: {m.Type}");
            }

            // Try to find mode with specific refresh rate first
            var mode = connector.Modes.FirstOrDefault(m => m.HDisplay == width && m.VDisplay == height && m.VRefresh == 60);
            if (mode == null)
            {
                Console.WriteLine($"{width}x{height}@60Hz mode not found! Looking for any {width}x{height} mode...");
                var matchingModes = connector.Modes.Where(m => m.HDisplay == width && m.VDisplay == height);
                foreach (var m in matchingModes)
                {
                    Console.WriteLine($"  {m.Name}: {m.HDisplay}x{m.VDisplay}@{m.VRefresh}Hz");
                }
                mode = matchingModes.FirstOrDefault();
                if (mode == null)
                {
                    Console.WriteLine($"No {width}x{height} mode found at all!");
                    return null;
                }
            }

            Console.WriteLine($"Using mode: {mode.Name} ({mode.HDisplay}x{mode.VDisplay}@{mode.VRefresh}Hz)");
            return mode;
        }

        private static uint FindCrtc(DrmConnector connector, DrmDeviceResources resources)
        {
            var encoder = connector.Encoder ?? connector.Encoders.FirstOrDefault();
            if (encoder == null)
            {
                Console.WriteLine("No encoder found for connector");
                return 0;
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
                Console.WriteLine("No available CRTC found");
            }

            return crtcId;
        }

        private static List<DrmPlane> GetCrtcCompatiblePlanes(DrmDeviceResources resources, int crtcIndex)
        {
            return resources.Planes
                .Where(p => (p.PossibleCrtcs & (1u << crtcIndex)) != 0)
                .ToList();
        }

        private static void LogPlaneInformation(DrmDeviceResources resources, uint crtcId, int crtcIndex, List<DrmPlane> crtcCompatiblePlanes)
        {
            Console.WriteLine($"=== DEBUG: All planes in system ===");
            Console.WriteLine($"Total planes: {resources.Planes.Count}");
            foreach (var pl in resources.Planes)
            {
                Console.WriteLine($"  Plane ID: {pl.Id}, Possible CRTCs mask: 0b{Convert.ToString(pl.PossibleCrtcs, 2).PadLeft(8, '0')}");
            }

            Console.WriteLine($"=== DEBUG: Planes compatible with CRTC {crtcId} (index {crtcIndex}) ===");
            Console.WriteLine($"Total compatible planes: {crtcCompatiblePlanes.Count}");

            foreach (var p in crtcCompatiblePlanes)
            {
                LogPlaneDetails(p, crtcId);
            }

            Console.WriteLine($"=== Finding planes for mode setting and NV12 display ===");
        }

        private static void LogPlaneDetails(DrmPlane plane, uint crtcId)
        {
            Console.WriteLine($"    Plane ID: {plane.Id}");
            Console.WriteLine($"    Possible CRTCs mask: 0b{Convert.ToString(plane.PossibleCrtcs, 2).PadLeft(8, '0')}");
            Console.WriteLine($"    Current CRTC ID: {(plane.CrtcId != 0 ? plane.CrtcId.ToString() : "none (inactive)")}");
            Console.WriteLine($"    Current FB ID: {(plane.FbId != 0 ? plane.FbId.ToString() : "none")}");

            var planeProperties = plane.GetProperties();
            var typeProp = planeProperties.FirstOrDefault(prop => prop.Name.Equals("type", StringComparison.OrdinalIgnoreCase));

            string planeType = GetPlaneType(plane, typeProp, crtcId);

            bool supportsRgb = plane.Formats.Any(fmt =>
                fmt == KnownPixelFormats.DRM_FORMAT_XRGB8888.Fourcc ||
                fmt == KnownPixelFormats.DRM_FORMAT_ARGB8888.Fourcc);
            bool supportsNv12 = plane.Formats.Contains(KnownPixelFormats.DRM_FORMAT_NV12.Fourcc);

            Console.WriteLine($"    Type: {planeType}");
            Console.WriteLine($"    Supports RGB (XRGB8888/ARGB8888): {supportsRgb}");
            Console.WriteLine($"    Supports NV12: {supportsNv12}");
            Console.WriteLine($"    Total formats supported: {plane.Formats.Count}");
            Console.WriteLine($"    Supported formats:");
            foreach (var fmt in plane.Formats)
            {
                var name = KnownPixelFormats.GetName(new(fmt));
                Console.WriteLine($"      - {name} (0x{fmt:X8})");
            }

            Console.WriteLine($"    Properties: {planeProperties.Count} total");
            foreach (var prop in planeProperties)
            {
                Console.WriteLine($"      - {prop.Name} (ID: {prop.Id})");
                Console.WriteLine($"        Type: {prop.Type}, Value: {prop.Value}");

                if (prop.Values != null && prop.Values.Count > 0)
                {
                    Console.WriteLine($"        Possible values: {string.Join(", ", prop.Values)}");
                }
            }
        }

        private static string GetPlaneType(DrmPlane plane, DrmProperty? typeProp, uint crtcId)
        {
            if (typeProp != null && typeProp.EnumNames != null && typeProp.Value < (ulong)typeProp.EnumNames.Count)
            {
                return typeProp.EnumNames[(int)typeProp.Value].ToUpperInvariant();
            }

            // Fallback to heuristic if type property not available
            bool isActive = plane.CrtcId == crtcId;
            bool hasRgb = plane.Formats.Any(fmt =>
                fmt == KnownPixelFormats.DRM_FORMAT_XRGB8888.Fourcc ||
                fmt == KnownPixelFormats.DRM_FORMAT_ARGB8888.Fourcc);

            if (isActive && hasRgb)
                return "PRIMARY (heuristic)";
            if (isActive)
                return "PRIMARY or OVERLAY (heuristic)";
            return "OVERLAY (heuristic)";
        }

        private static DrmPlane? FindPrimaryPlane(List<DrmPlane> crtcCompatiblePlanes)
        {
            return crtcCompatiblePlanes.FirstOrDefault(p =>
            {
                var props = p.GetProperties();
                var typeProp = props.FirstOrDefault(prop => prop.Name.Equals("type", StringComparison.OrdinalIgnoreCase));
                return typeProp != null && typeProp.EnumNames != null &&
                       typeProp.Value < (ulong)typeProp.EnumNames.Count &&
                       typeProp.EnumNames[(int)typeProp.Value].Equals("Primary", StringComparison.OrdinalIgnoreCase);
            });
        }

        private static DrmPlane? FindNv12OverlayPlane(List<DrmPlane> crtcCompatiblePlanes)
        {
            var nv12Format = KnownPixelFormats.DRM_FORMAT_NV12.Fourcc;
            return crtcCompatiblePlanes.FirstOrDefault(p =>
            {
                var props = p.GetProperties();
                var typeProp = props.FirstOrDefault(prop => prop.Name.Equals("type", StringComparison.OrdinalIgnoreCase));
                bool isOverlay = typeProp != null && typeProp.EnumNames != null &&
                                typeProp.Value < (ulong)typeProp.EnumNames.Count &&
                                typeProp.EnumNames[(int)typeProp.Value].Equals("Overlay", StringComparison.OrdinalIgnoreCase);
                return isOverlay && p.Formats.Contains(nv12Format);
            });
        }

        private static DmaBuffers.DmaBuffer? CreateRgbBuffer(DmaBuffersAllocator allocator, int width, int height)
        {
            ulong rgbBufferSize = (ulong)(width * height * 4); // XRGB8888 = 4 bytes per pixel
            var rgbBuf = allocator.Allocate(rgbBufferSize);
            if (rgbBuf == null)
            {
                Console.WriteLine("Failed to allocate RGB buffer.");
                return null;
            }
            Console.WriteLine($"Allocated RGB buffer of size {rgbBuf.Size} with fd {rgbBuf.Fd}");

            rgbBuf.MapBuffer();
            if (rgbBuf.MapStatus == MapStatus.FailedToMap)
            {
                Console.WriteLine("Failed to mmap RGB buffer.");
                rgbBuf.Dispose();
                return null;
            }

            var rgbSpan = rgbBuf.GetMappedSpan();
            rgbSpan.Fill(0); // Fill with black
            rgbBuf.SyncMap();
            Console.WriteLine("Filled RGB buffer with black");

            return rgbBuf;
        }

        private static unsafe (uint fbId, uint handle) CreateRgbFramebuffer(DrmDevice drmDevice, DmaBuffers.DmaBuffer rgbBuf, int width, int height)
        {
            var result = LibDrm.drmPrimeFDToHandle(drmDevice.DeviceFd, rgbBuf.Fd, out uint rgbHandle);
            if (result != 0)
            {
                Console.WriteLine($"Failed to convert RGB DMA FD to handle: {result}");
                return (0, 0);
            }
            Console.WriteLine($"Converted RGB DMA FD {rgbBuf.Fd} to handle {rgbHandle}");

            var rgbFormat = KnownPixelFormats.DRM_FORMAT_XRGB8888.Fourcc;
            uint rgbPitch = (uint)(width * 4);
            uint* rgbHandles = stackalloc uint[4] { rgbHandle, 0, 0, 0 };
            uint* rgbPitches = stackalloc uint[4] { rgbPitch, 0, 0, 0 };
            uint* rgbOffsets = stackalloc uint[4] { 0, 0, 0, 0 };

            var resultFb = LibDrm.drmModeAddFB2(drmDevice.DeviceFd, (uint)width, (uint)height, rgbFormat,
                                               rgbHandles, rgbPitches, rgbOffsets, out var rgbFbId, 0);
            if (resultFb != 0)
            {
                Console.WriteLine($"Failed to create RGB framebuffer: {resultFb}");
                return (0, 0);
            }
            Console.WriteLine($"Created RGB framebuffer with ID: {rgbFbId}");

            return (rgbFbId, rgbHandle);
        }

        private static unsafe bool SetCrtcMode(DrmDevice drmDevice, uint crtcId, uint connectorId, uint fbId, DrmModeInfo mode)
        {
            var nativeMode = ConvertToNativeMode(mode);
            var result = LibDrm.drmModeSetCrtc(drmDevice.DeviceFd, crtcId, fbId, 0, 0, &connectorId, 1, &nativeMode);

            if (result != 0)
            {
                Console.WriteLine($"Failed to set CRTC mode: {result}");
                return false;
            }

            Console.WriteLine($"Successfully set CRTC to mode {mode.Name} ({mode.HDisplay}x{mode.VDisplay}@{mode.VRefresh}Hz)");
            return true;
        }

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
                var nameBytes = System.Text.Encoding.UTF8.GetBytes(mode.Name);
                for (int i = 0; i < Math.Min(nameBytes.Length, 32); i++)
                {
                    nativeMode.Name[i] = nameBytes[i];
                }
            }

            return nativeMode;
        }

        private static unsafe bool SetPlane(int drmFd, uint planeId, uint crtcId, uint fbId, int width, int height)
        {
            var result = LibDrm.drmModeSetPlane(drmFd, planeId, crtcId, fbId, 0,
                                               0, 0, (uint)width, (uint)height,
                                               0, 0, (uint)width << 16, (uint)height << 16);
            if (result != 0)
            {
                Console.WriteLine($"Failed to set plane {planeId}: {result}");
                return false;
            }
            return true;
        }

        private static unsafe uint CreateNv12Framebuffer(DrmDevice drmDevice, DmaBuffers.DmaBuffer nv12Buffer, int width, int height)
        {
            var result = LibDrm.drmPrimeFDToHandle(drmDevice.DeviceFd, nv12Buffer.Fd, out uint nv12Handle);
            if (result != 0)
            {
                Console.WriteLine($"Failed to convert NV12 DMA FD to handle: {result}");
                return 0;
            }
            Console.WriteLine($"Converted NV12 DMA FD {nv12Buffer.Fd} to handle {nv12Handle}");

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
                Console.WriteLine($"Failed to create NV12 framebuffer: {resultFb}");
                return 0;
            }
            Console.WriteLine($"Created NV12 framebuffer with ID: {nv12FbId}");

            return nv12FbId;
        }

        private static bool PresentNv12Buffer(DrmDevice drmDevice, DmaBuffers.DmaBuffer nv12Buffer, int width, int height, DmaBuffersAllocator allocator)
        {
            var resources = drmDevice.GetResources();
            if (resources == null)
            {
                Console.WriteLine("Failed to get DRM resources");
                return false;
            }

            var connector = FindConnectedConnector(resources);
            if (connector == null)
            {
                return false;
            }

            var mode = FindDisplayMode(connector, width, height);
            if (mode == null)
            {
                return false;
            }

            var crtcId = FindCrtc(connector, resources);
            if (crtcId == 0)
            {
                return false;
            }

            Console.WriteLine($"Using CRTC ID: {crtcId}");

            var crtcIndex = resources.Crtcs.ToList().IndexOf(crtcId);
            var crtcCompatiblePlanes = GetCrtcCompatiblePlanes(resources, crtcIndex);

            LogPlaneInformation(resources, crtcId, crtcIndex, crtcCompatiblePlanes);

            var primaryPlane = FindPrimaryPlane(crtcCompatiblePlanes);
            if (primaryPlane == null)
            {
                Console.WriteLine("ERROR: No primary plane found for CRTC mode setting.");
                return false;
            }
            Console.WriteLine($"Found primary plane: ID {primaryPlane.Id}");

            var nv12Plane = FindNv12OverlayPlane(crtcCompatiblePlanes);
            if (nv12Plane == null)
            {
                Console.WriteLine("ERROR: No overlay plane found that supports NV12 format.");
                return false;
            }
            Console.WriteLine($"Found NV12-capable overlay plane: ID {nv12Plane.Id}");

            return SetupDisplay(drmDevice, connector, mode, crtcId, primaryPlane, nv12Plane, nv12Buffer, width, height, allocator);
        }

        private static unsafe bool SetupDisplay(
            DrmDevice drmDevice,
            DrmConnector connector,
            DrmModeInfo mode,
            uint crtcId,
            DrmPlane primaryPlane,
            DrmPlane nv12Plane,
            DmaBuffers.DmaBuffer nv12Buffer,
            int width,
            int height,
            DmaBuffersAllocator allocator)
        {
            Console.WriteLine("=== Step 1: Setting up RGB buffer for mode setting ===");
            var rgbBuf = CreateRgbBuffer(allocator, width, height);
            if (rgbBuf == null)
            {
                return false;
            }

            var (rgbFbId, rgbHandle) = CreateRgbFramebuffer(drmDevice, rgbBuf, width, height);
            if (rgbFbId == 0)
            {
                rgbBuf.Dispose();
                return false;
            }

            Console.WriteLine($"=== Step 2: Setting CRTC to {width}x{height} mode ===");
            if (!SetCrtcMode(drmDevice, crtcId, connector.ConnectorId, rgbFbId, mode))
            {
                LibDrm.drmModeRmFB(drmDevice.DeviceFd, rgbFbId);
                rgbBuf.Dispose();
                return false;
            }

            Console.WriteLine($"=== Step 3: Setting primary plane with RGB buffer ===");
            if (!SetPlane(drmDevice.DeviceFd, primaryPlane.Id, crtcId, rgbFbId, width, height))
            {
                LibDrm.drmModeRmFB(drmDevice.DeviceFd, rgbFbId);
                rgbBuf.Dispose();
                return false;
            }
            Console.WriteLine("Successfully set primary plane with RGB framebuffer");

            Console.WriteLine($"=== Step 4: Setting up NV12 overlay ===");
            var nv12FbId = CreateNv12Framebuffer(drmDevice, nv12Buffer, width, height);
            if (nv12FbId == 0)
            {
                LibDrm.drmModeRmFB(drmDevice.DeviceFd, rgbFbId);
                rgbBuf.Dispose();
                return false;
            }

            if (!SetPlane(drmDevice.DeviceFd, nv12Plane.Id, crtcId, nv12FbId, width, height))
            {
                LibDrm.drmModeRmFB(drmDevice.DeviceFd, nv12FbId);
                LibDrm.drmModeRmFB(drmDevice.DeviceFd, rgbFbId);
                rgbBuf.Dispose();
                return false;
            }

            Console.WriteLine("Successfully set NV12 overlay plane!");
            Console.WriteLine("=== Display Setup Complete ===");
            Console.WriteLine($"  Primary plane (ID {primaryPlane.Id}): RGB framebuffer at {width}x{height}");
            Console.WriteLine($"  Overlay plane (ID {nv12Plane.Id}): NV12 content covering full screen");

            rgbBuf.UnmapBuffer();
            return true;
        }
    }
}
