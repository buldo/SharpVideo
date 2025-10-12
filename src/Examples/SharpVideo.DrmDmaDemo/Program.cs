using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;
using System.Runtime.Versioning;

namespace SharpVideo.DrmDmaDemo
{
    internal class Program
    {
        [SupportedOSPlatform("linux")]
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
            if (PresentBuffer(drmDevice, dmaBuf, width, height))
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

        [SupportedOSPlatform("linux")]
        private static bool PresentBuffer(DrmDevice drmDevice, DmaBuffers.DmaBuffer nv12Buffer, int width, int height)
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

            // Query and display DRM capabilities
            Console.WriteLine("\n=== DRM Device Capabilities ===");
            var capabilities = new[]
            {
                DrmCapability.DumbBuffer,
                DrmCapability.VblankHighCrtc,
                DrmCapability.DumbPreferredDepth,
                DrmCapability.DumbPreferShadow,
                DrmCapability.Prime,
                DrmCapability.TimestampMonotonic,
                DrmCapability.AsyncPageFlip,
                DrmCapability.CursorWidth,
                DrmCapability.CursorHeight,
                DrmCapability.AddFB2Modifiers,
                DrmCapability.PageFlipTarget,
                DrmCapability.CrtcInVblankEvent,
                DrmCapability.SyncObj,
                DrmCapability.SyncObjTimeline,
                DrmCapability.AtomicAsyncPageFlip
            };

            foreach (var cap in capabilities)
            {
                var result = LibDrm.drmGetCap(drmDevice.DeviceFd, cap, out ulong value);
                if (result == 0)
                {
                    string displayValue = cap switch
                    {
                        DrmCapability.Prime => value switch
                        {
                            0 => "0 (not supported)",
                            1 => "1 (import only)",
                            2 => "2 (export only)",
                            3 => "3 (import & export)",
                            _ => value.ToString()
                        },
                        DrmCapability.DumbBuffer or
                        DrmCapability.VblankHighCrtc or
                        DrmCapability.DumbPreferShadow or
                        DrmCapability.TimestampMonotonic or
                        DrmCapability.AsyncPageFlip or
                        DrmCapability.AddFB2Modifiers or
                        DrmCapability.PageFlipTarget or
                        DrmCapability.CrtcInVblankEvent or
                        DrmCapability.SyncObj or
                        DrmCapability.SyncObjTimeline or
                        DrmCapability.AtomicAsyncPageFlip => value == 1 ? "Yes" : "No",
                        _ => value.ToString()
                    };
                    Console.WriteLine($"  {cap}: {displayValue}");
                }
                else
                {
                    Console.WriteLine($"  {cap}: <query failed: {result}>");
                }
            }

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

            Console.WriteLine($"\nUsing CRTC ID: {crtcId}");

            // Display all planes in the system first
            Console.WriteLine($"\n=== DEBUG: All planes in system ===");
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

            Console.WriteLine($"\n=== DEBUG: Planes compatible with CRTC {crtcId} (index {crtcIndex}) ===");
            Console.WriteLine($"Total compatible planes: {crtcCompatiblePlanes.Count}");

            foreach (var p in crtcCompatiblePlanes)
            {
                Console.WriteLine($"\n  Plane ID: {p.Id}");
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

            // Find a plane that supports NV12 format and is compatible with this CRTC
            var nv12Format = KnownPixelFormats.DRM_FORMAT_NV12.Fourcc;
            Console.WriteLine($"\n=== Selecting plane for NV12 display (format 0x{nv12Format:X8}) ===");

            var nv12Planes = crtcCompatiblePlanes
                .Where(p => p.Formats.Contains(nv12Format))
                .ToList();

            Console.WriteLine($"Found {nv12Planes.Count} NV12-capable planes");

            // Prefer overlay planes (not currently active) over primary plane
            var plane = nv12Planes.FirstOrDefault(p => p.CrtcId != crtcId) ?? nv12Planes.FirstOrDefault();
            if (plane == null)
            {
                Console.WriteLine("ERROR: No plane found that supports NV12 format for the selected CRTC.");
                return false;
            }

            Console.WriteLine($"Selected plane ID {plane.Id} for NV12 display");
            Console.WriteLine($"  Currently active: {(plane.CrtcId != 0 ? "yes (on CRTC " + plane.CrtcId + ")" : "no")}");

            unsafe
            {
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

                // Check if CRTC is already active and get current mode
                var crtcInfo = LibDrm.drmModeGetCrtc(drmDevice.DeviceFd, crtcId);

                if (crtcInfo == null || crtcInfo->BufferId == 0)
                {
                    Console.WriteLine("\nERROR: CRTC is not active.");
                    Console.WriteLine("The display must be active (initialized by bootloader/kernel) to use plane overlays.");
                    Console.WriteLine("NV12 can only be displayed on overlay planes, not on the primary CRTC plane.");
                    if (crtcInfo != null)
                    {
                        LibDrm.drmModeFreeCrtc(crtcInfo);
                    }
                    LibDrm.drmModeRmFB(drmDevice.DeviceFd, nv12FbId);
                    return false;
                }

                Console.WriteLine($"\nCurrent CRTC state: BufferId={crtcInfo->BufferId}, Mode={crtcInfo->Mode.HDisplay}x{crtcInfo->Mode.VDisplay}");

                // Check if current mode matches our requirement
                if (crtcInfo->Mode.HDisplay != mode.HDisplay || crtcInfo->Mode.VDisplay != mode.VDisplay)
                {
                    Console.WriteLine($"\nERROR: Display is at {crtcInfo->Mode.HDisplay}x{crtcInfo->Mode.VDisplay}, but we need {mode.HDisplay}x{mode.VDisplay}");
                    Console.WriteLine("Cannot change display mode using NV12-only buffer.");
                    Console.WriteLine("Explanation: CRTC (display controller) only accepts RGB formats for mode changes.");
                    Console.WriteLine("             NV12 (YUV format) can only be used on overlay planes.");
                    Console.WriteLine("Solutions:");
                    Console.WriteLine("  1. Set display to 1080p before running this program (using xrandr, kernel cmdline, etc.)");
                    Console.WriteLine("  2. Modify program to allocate RGB buffer for CRTC mode setting");
                    LibDrm.drmModeFreeCrtc(crtcInfo);
                    LibDrm.drmModeRmFB(drmDevice.DeviceFd, nv12FbId);
                    return false;
                }

                Console.WriteLine($"CRTC already at required resolution {mode.HDisplay}x{mode.VDisplay}");

                LibDrm.drmModeFreeCrtc(crtcInfo);

                // Now set the plane with the NV12 framebuffer as an overlay
                Console.WriteLine($"\nSetting plane overlay with NV12 content at {width}x{height}");
                var result = LibDrm.drmModeSetPlane(drmDevice.DeviceFd, plane.Id, crtcId, nv12FbId, 0,
                                               0, 0, (uint)width, (uint)height,  // Display area (CRTC coordinates)
                                               0, 0, (uint)width << 16, (uint)height << 16);  // Source area (framebuffer coordinates)
                if (result != 0)
                {
                    Console.WriteLine($"Failed to set plane overlay: {result}");
                    LibDrm.drmModeRmFB(drmDevice.DeviceFd, nv12FbId);
                    return false;
                }

                Console.WriteLine("Successfully set plane overlay with NV12 framebuffer!");

                Console.WriteLine("Successfully set CRTC and displayed image");
                return true;
            }
        }
    }
}
