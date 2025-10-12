using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;
using System.Runtime.Versioning;

namespace SharpVideo.DrmDmaDemo
{
    [SupportedOSPlatform("linux")]
    internal class Program
    {
        static void Main(string[] args)
        {
            var devices = Directory.EnumerateFiles("/dev/dri", "card*", SearchOption.TopDirectoryOnly);
            DrmDevice? drmDevice = null;
            foreach (var device in devices)
            {
                drmDevice = DrmDevice.Open(device);
                if (drmDevice != null)
                {
                    Console.WriteLine($"Opened DRM device: {device}");
                    break;
                }
                else
                {
                    Console.WriteLine($"Failed to open DRM device: {device}");
                }
            }

            if (drmDevice == null)
            {
                Console.WriteLine("No DRM devices could be opened.");
                return;
            }

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

            DumpDeviceCaps(drmDevice);

            if (!DmaBuffersAllocator.TryCreate(out var allocator) || allocator == null)
            {
                Console.WriteLine("Failed to create DMA buffers allocator.");
                return;
            }

            // Use 1080p resolution for the buffer
            const int width = 1920;
            const int height = 1080;

            // Allocate NV12 format buffer only (no RGB)
            ulong nv12BufferSize = (ulong)(width * height * 3.0 / 2.0);
            var dmaBuf = allocator.Allocate(nv12BufferSize);
            if (dmaBuf == null)
            {
                Console.WriteLine("Failed to allocate NV12 DMA buffer.");
                return;
            }

            Console.WriteLine($"Allocated NV12 buffer of size {dmaBuf.Size} with fd {dmaBuf.Fd}");

            dmaBuf.MapBuffer();

            if (dmaBuf.MapStatus == MapStatus.FailedToMap)
            {
                Console.WriteLine("Failed to mmap DMA buffer.");
                dmaBuf.Dispose();
                return;
            }

            Console.WriteLine($"DMA buffer mapped");

            // Fill with test pattern
            TestPattern.FillNV12(dmaBuf.GetMappedSpan(), width, height);

            Console.WriteLine("Filled DMA buffer with NV12 test pattern.");

            dmaBuf.SyncMap();
            Console.WriteLine("Synced DMA buffer.");

            // Make the buffer read-only to prevent accidental modification
            var roResult = dmaBuf.MakeMapReadOnly();
            Console.WriteLine(roResult
                ? "Made buffer read-only to prevent modification."
                : "Warning: Failed to make buffer read-only, continuing anyway.");

            // Present the buffer on the display
            if (PresentBuffer(drmDevice, dmaBuf, width, height, allocator))
            {
                Console.WriteLine("Successfully presented buffer on display.");

                // Keep the display active and the buffer mapped for the entire duration
                Console.WriteLine("Displaying pattern for 10 seconds...");
                Thread.Sleep(10000);

                dmaBuf.UnmapBuffer();
                Console.WriteLine("Unmapped DMA buffer.");
            }
            else
            {
                Console.WriteLine("Failed to present buffer on display.");
                // Unmap on failure
                dmaBuf.UnmapBuffer();
            }

            dmaBuf.Dispose();
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

        private static bool PresentBuffer(DrmDevice drmDevice, DmaBuffers.DmaBuffer nv12Buffer, int width, int height, DmaBuffersAllocator allocator)
        {
            var resources = drmDevice.GetResources();
            if (resources == null)
            {
                Console.WriteLine("Failed to get DRM resources");
                return false;
            }

            // Find a connected connector
            var connector = resources.Connectors.FirstOrDefault(c => c.Connection == DrmModeConnection.Connected);
            if (connector == null)
            {
                Console.WriteLine("No connected display found");
                return false;
            }

            Console.WriteLine($"Found connected display: {connector.ConnectorType}");

            // Debug: Print all available modes
            Console.WriteLine($"Available modes ({connector.Modes.Count}):");
            foreach (var m in connector.Modes)
            {
                Console.WriteLine($"  {m.Name}: {m.HDisplay}x{m.VDisplay}@{m.VRefresh}Hz, Type: {m.Type}");
            }

            // Find 1080p@60Hz mode specifically
            var mode = connector.Modes.FirstOrDefault(m => m.HDisplay == 1920 && m.VDisplay == 1080 && m.VRefresh == 60);
            if (mode == null)
            {
                Console.WriteLine("1080p@60Hz mode not found! Available 1080p modes:");
                var modes1080p = connector.Modes.Where(m => m.HDisplay == 1920 && m.VDisplay == 1080);
                foreach (var m in modes1080p)
                {
                    Console.WriteLine($"  {m.Name}: {m.HDisplay}x{m.VDisplay}@{m.VRefresh}Hz");
                }
                // Fallback to any 1080p mode
                mode = modes1080p.FirstOrDefault();
                if (mode == null)
                {
                    Console.WriteLine("No 1080p mode found at all!");
                    return false;
                }
            }

            Console.WriteLine($"Using mode: {mode.Name} ({mode.HDisplay}x{mode.VDisplay}@{mode.VRefresh}Hz)");

            // Find an encoder and CRTC
            var encoder = connector.Encoder;
            if (encoder == null && connector.Encoders.Any())
            {
                encoder = connector.Encoders.First();
            }

            if (encoder == null)
            {
                Console.WriteLine("No encoder found for connector");
                return false;
            }

            // Find an available CRTC
            var crtcId = encoder.CrtcId;
            if (crtcId == 0)
            {
                // Find the first available CRTC
                var availableCrtcs = resources.Crtcs.Where(crtc => (encoder.PossibleCrtcs & (1u << (int)Array.IndexOf(resources.Crtcs.ToArray(), crtc))) != 0);
                crtcId = availableCrtcs.FirstOrDefault();
            }

            if (crtcId == 0)
            {
                Console.WriteLine("No available CRTC found");
                return false;
            }

            Console.WriteLine($"Using CRTC ID: {crtcId}");

            // Display all planes in the system first
            Console.WriteLine($"=== DEBUG: All planes in system ===");
            Console.WriteLine($"Total planes: {resources.Planes.Count}");
            foreach (var pl in resources.Planes)
            {
                Console.WriteLine($"  Plane ID: {pl.Id}, Possible CRTCs mask: 0b{Convert.ToString(pl.PossibleCrtcs, 2).PadLeft(8, '0')}");
            }

            // Display all planes for this CRTC with detailed information
            var crtcIndex = resources.Crtcs.ToList().IndexOf(crtcId);
            var crtcCompatiblePlanes = resources.Planes
                .Where(p => (p.PossibleCrtcs & (1u << crtcIndex)) != 0)
                .ToList();

            Console.WriteLine($"=== DEBUG: Planes compatible with CRTC {crtcId} (index {crtcIndex}) ===");
            Console.WriteLine($"Total compatible planes: {crtcCompatiblePlanes.Count}");

            foreach (var p in crtcCompatiblePlanes)
            {
                Console.WriteLine($"    Plane ID: {p.Id}");
                Console.WriteLine($"    Possible CRTCs mask: 0b{Convert.ToString(p.PossibleCrtcs, 2).PadLeft(8, '0')}");
                Console.WriteLine($"    Current CRTC ID: {(p.CrtcId != 0 ? p.CrtcId.ToString() : "none (inactive)")}");
                Console.WriteLine($"    Current FB ID: {(p.FbId != 0 ? p.FbId.ToString() : "none")}");

                // Get plane properties to determine type and show all properties
                var planeProperties = p.GetProperties();
                var typeProp = planeProperties.FirstOrDefault(prop => prop.Name.Equals("type", StringComparison.OrdinalIgnoreCase));

                string planeType = "UNKNOWN";
                if (typeProp != null && typeProp.EnumNames != null && typeProp.Value < (ulong)typeProp.EnumNames.Count)
                {
                    planeType = typeProp.EnumNames[(int)typeProp.Value].ToUpperInvariant();
                }
                else
                {
                    // Fallback to heuristic if type property not available
                    bool isActive = p.CrtcId == crtcId;
                    bool hasRgb = p.Formats.Any(fmt =>
                        fmt == KnownPixelFormats.DRM_FORMAT_XRGB8888.Fourcc ||
                        fmt == KnownPixelFormats.DRM_FORMAT_ARGB8888.Fourcc);

                    if (isActive && hasRgb)
                    {
                        planeType = "PRIMARY (heuristic)";
                    }
                    else if (isActive)
                    {
                        planeType = "PRIMARY or OVERLAY (heuristic)";
                    }
                    else
                    {
                        planeType = "OVERLAY (heuristic)";
                    }
                }

                bool supportsRgb = p.Formats.Any(fmt =>
                    fmt == KnownPixelFormats.DRM_FORMAT_XRGB8888.Fourcc ||
                    fmt == KnownPixelFormats.DRM_FORMAT_ARGB8888.Fourcc);
                bool supportsNv12 = p.Formats.Contains(KnownPixelFormats.DRM_FORMAT_NV12.Fourcc);

                Console.WriteLine($"    Type: {planeType}");
                Console.WriteLine($"    Supports RGB (XRGB8888/ARGB8888): {supportsRgb}");
                Console.WriteLine($"    Supports NV12: {supportsNv12}");
                Console.WriteLine($"    Total formats supported: {p.Formats.Count}");
                Console.WriteLine($"    Supported formats:");
                foreach (var fmt in p.Formats)
                {
                    var name = KnownPixelFormats.GetName(new(fmt));
                    Console.WriteLine($"      - {name} (0x{fmt:X8})");
                }

                // Print plane properties
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

            // Find primary plane and overlay plane
            Console.WriteLine($"=== Finding planes for mode setting and NV12 display ===");

            // Find primary plane (for mode setting with RGB)
            var primaryPlane = crtcCompatiblePlanes
                .FirstOrDefault(p =>
                {
                    var props = p.GetProperties();
                    var typeProp = props.FirstOrDefault(prop => prop.Name.Equals("type", StringComparison.OrdinalIgnoreCase));
                    return typeProp != null && typeProp.EnumNames != null &&
                           typeProp.Value < (ulong)typeProp.EnumNames.Count &&
                           typeProp.EnumNames[(int)typeProp.Value].Equals("Primary", StringComparison.OrdinalIgnoreCase);
                });

            if (primaryPlane == null)
            {
                Console.WriteLine("ERROR: No primary plane found for CRTC mode setting.");
                return false;
            }

            Console.WriteLine($"Found primary plane: ID {primaryPlane.Id}");

            // Find overlay plane for NV12
            var nv12Format = KnownPixelFormats.DRM_FORMAT_NV12.Fourcc;
            var nv12Plane = crtcCompatiblePlanes
                .FirstOrDefault(p =>
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
                Console.WriteLine("ERROR: No overlay plane found that supports NV12 format.");
                return false;
            }

            Console.WriteLine($"Found NV12-capable overlay plane: ID {nv12Plane.Id}");

            unsafe
            {
                // Step 1: Allocate RGB buffer for primary plane (mode setting)
                Console.WriteLine("=== Step 1: Setting up RGB buffer for mode setting ===");
                ulong rgbBufferSize = (ulong)(width * height * 4); // XRGB8888 = 4 bytes per pixel
                var rgbBuf = allocator.Allocate(rgbBufferSize);
                if (rgbBuf == null)
                {
                    Console.WriteLine("Failed to allocate RGB buffer.");
                    return false;
                }
                Console.WriteLine($"Allocated RGB buffer of size {rgbBuf.Size} with fd {rgbBuf.Fd}");

                // Map and fill with black (or any solid color to see primary plane)
                rgbBuf.MapBuffer();
                if (rgbBuf.MapStatus == MapStatus.FailedToMap)
                {
                    Console.WriteLine("Failed to mmap RGB buffer.");
                    rgbBuf.Dispose();
                    return false;
                }

                var rgbSpan = rgbBuf.GetMappedSpan();
                rgbSpan.Fill(0); // Fill with black
                rgbBuf.SyncMap();
                Console.WriteLine("Filled RGB buffer with black");

                // Convert RGB DMA buffer FD to DRM handle
                var resultRgb = LibDrm.drmPrimeFDToHandle(drmDevice.DeviceFd, rgbBuf.Fd, out uint rgbHandle);
                if (resultRgb != 0)
                {
                    Console.WriteLine($"Failed to convert RGB DMA FD to handle: {resultRgb}");
                    rgbBuf.Dispose();
                    return false;
                }
                Console.WriteLine($"Converted RGB DMA FD {rgbBuf.Fd} to handle {rgbHandle}");

                // Create RGB framebuffer
                var rgbFormat = KnownPixelFormats.DRM_FORMAT_XRGB8888.Fourcc;
                uint rgbPitch = (uint)(width * 4);
                uint* rgbHandles = stackalloc uint[4] { rgbHandle, 0, 0, 0 };
                uint* rgbPitches = stackalloc uint[4] { rgbPitch, 0, 0, 0 };
                uint* rgbOffsets = stackalloc uint[4] { 0, 0, 0, 0 };
                var resultRgbFb = LibDrm.drmModeAddFB2(drmDevice.DeviceFd, (uint)width, (uint)height, rgbFormat, rgbHandles, rgbPitches, rgbOffsets, out var rgbFbId, 0);
                if (resultRgbFb != 0)
                {
                    Console.WriteLine($"Failed to create RGB framebuffer: {resultRgbFb}");
                    rgbBuf.Dispose();
                    return false;
                }
                Console.WriteLine($"Created RGB framebuffer with ID: {rgbFbId}");

                // Set the CRTC mode to 1080p
                Console.WriteLine($"=== Step 2: Setting CRTC to 1080p mode ===");

                // Convert managed DrmModeInfo to native DrmModeModeInfo
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

                // Copy the mode name
                var nameBytes = System.Text.Encoding.UTF8.GetBytes(mode.Name);
                for (int i = 0; i < Math.Min(nameBytes.Length, 32); i++)
                {
                    nativeMode.Name[i] = nameBytes[i];
                }

                uint connectorId = connector.ConnectorId;
                var resultSetCrtc = LibDrm.drmModeSetCrtc(drmDevice.DeviceFd, crtcId, rgbFbId, 0, 0,
                                                          &connectorId, 1, &nativeMode);
                if (resultSetCrtc != 0)
                {
                    Console.WriteLine($"Failed to set CRTC mode: {resultSetCrtc}");
                    LibDrm.drmModeRmFB(drmDevice.DeviceFd, rgbFbId);
                    rgbBuf.Dispose();
                    return false;
                }
                Console.WriteLine($"Successfully set CRTC to mode {mode.Name} ({mode.HDisplay}x{mode.VDisplay}@{mode.VRefresh}Hz)");

                // Set the primary plane with RGB buffer
                Console.WriteLine($"=== Step 3: Setting primary plane with RGB buffer ===");
                var resultPrimary = LibDrm.drmModeSetPlane(drmDevice.DeviceFd, primaryPlane.Id, crtcId, rgbFbId, 0,
                                                     0, 0, (uint)width, (uint)height,
                                                     0, 0, (uint)width << 16, (uint)height << 16);
                if (resultPrimary != 0)
                {
                    Console.WriteLine($"Failed to set primary plane: {resultPrimary}");
                    LibDrm.drmModeRmFB(drmDevice.DeviceFd, rgbFbId);
                    rgbBuf.Dispose();
                    return false;
                }
                Console.WriteLine("Successfully set primary plane with RGB framebuffer at 1080p");

                // Step 4: Now set up NV12 overlay
                Console.WriteLine($"=== Step 4: Setting up NV12 overlay ===");
                // Convert NV12 DMA buffer FD to DRM handle
                var resultNv12 = LibDrm.drmPrimeFDToHandle(drmDevice.DeviceFd, nv12Buffer.Fd, out uint nv12Handle);
                if (resultNv12 != 0)
                {
                    Console.WriteLine($"Failed to convert NV12 DMA FD to handle: {resultNv12}");
                    return false;
                }
                Console.WriteLine($"Converted NV12 DMA FD {nv12Buffer.Fd} to handle {nv12Handle}");

                // Create NV12 framebuffer
                // NV12 has 2 planes: Y plane and interleaved UV plane
                uint yPitch = (uint)width;
                uint uvPitch = (uint)width;
                uint yOffset = 0;
                uint uvOffset = (uint)(width * height);
                uint* nv12Handles = stackalloc uint[4] { nv12Handle, nv12Handle, 0, 0 };
                uint* nv12Pitches = stackalloc uint[4] { yPitch, uvPitch, 0, 0 };
                uint* nv12Offsets = stackalloc uint[4] { yOffset, uvOffset, 0, 0 };
                var resultNv12Fb = LibDrm.drmModeAddFB2(drmDevice.DeviceFd, (uint)width, (uint)height, nv12Format, nv12Handles, nv12Pitches, nv12Offsets, out var nv12FbId, 0);
                if (resultNv12Fb != 0)
                {
                    Console.WriteLine($"Failed to create NV12 framebuffer: {resultNv12Fb}");
                    return false;
                }
                Console.WriteLine($"Created NV12 framebuffer with ID: {nv12FbId}");

                // Now set the NV12 overlay plane
                Console.WriteLine($"Setting NV12 overlay plane at {width}x{height}");
                var resultOverlay = LibDrm.drmModeSetPlane(drmDevice.DeviceFd, nv12Plane.Id, crtcId, nv12FbId, 0,
                                               0, 0, (uint)width, (uint)height,  // Display area (CRTC coordinates)
                                               0, 0, (uint)width << 16, (uint)height << 16);  // Source area (framebuffer coordinates)
                if (resultOverlay != 0)
                {
                    Console.WriteLine($"Failed to set NV12 overlay plane: {resultOverlay}");
                    LibDrm.drmModeRmFB(drmDevice.DeviceFd, nv12FbId);
                    LibDrm.drmModeRmFB(drmDevice.DeviceFd, rgbFbId);
                    rgbBuf.Dispose();
                    return false;
                }

                Console.WriteLine("Successfully set NV12 overlay plane!");
                Console.WriteLine("=== Display Setup Complete ===");
                Console.WriteLine($"  Primary plane (ID {primaryPlane.Id}): RGB framebuffer at 1080p");
                Console.WriteLine($"  Overlay plane (ID {nv12Plane.Id}): NV12 content covering full screen");

                // Keep RGB buffer alive - we'll clean it up when done displaying
                // Note: In a real app, you'd want to manage this differently
                rgbBuf.UnmapBuffer();

                return true;
            }
        }
    }
}
