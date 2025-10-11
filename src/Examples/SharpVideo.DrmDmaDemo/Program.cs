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
            // NV12 format: Y plane (width * height) + UV plane (width * height / 2)
            ulong bufferSize = (ulong)(width * height * 3.0 / 2.0);

            var dmaBuf = allocator.Allocate(bufferSize);
            if (dmaBuf == null)
            {
                Console.WriteLine("Failed to allocate DMA buffer.");
                return;
            }

            Console.WriteLine($"Allocated DMA buffer of size {dmaBuf.Size} with fd {dmaBuf.Fd}");

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
        private static bool PresentBuffer(DrmDevice drmDevice, DmaBuffers.DmaBuffer dmaBuffer, int width, int height)
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

            var plane = resources.Planes.FirstOrDefault(p => (p.PossibleCrtcs & (1u << (int)resources.Crtcs.ToList().IndexOf(crtcId))) != 0);
            if (plane == null)
            {
                Console.WriteLine("No suitable plane found for the selected CRTC.");
                return false;
            }

            Console.WriteLine($"Found suitable plane with ID: {plane.Id}");
            Console.WriteLine("Plane supports the following formats:");
            foreach (var format in plane.Formats)
            {
                Console.WriteLine($"  - {KnownPixelFormats.GetName(new(format))} (0x{format:X})");
            }

            Console.WriteLine($"Using CRTC ID: {crtcId}");

            unsafe
            {
                // Convert DMA buffer FD to DRM handle
                var result = LibDrm.drmPrimeFDToHandle(drmDevice.DeviceFd, dmaBuffer.Fd, out uint handle);
                if (result != 0)
                {
                    Console.WriteLine($"Failed to convert DMA FD to handle: {result}");
                    return false;
                }

                Console.WriteLine($"Converted DMA FD {dmaBuffer.Fd} to handle {handle}");

                // Create framebuffer for NV12 format
                // NV12 has 2 planes: Y plane and interleaved UV plane
                uint yPitch = (uint)width;
                uint uvPitch = (uint)width;
                uint yOffset = 0;
                uint uvOffset = (uint)(width * height);
                uint* handles = stackalloc uint[4] { handle, handle, 0, 0 };
                uint* pitches = stackalloc uint[4] { yPitch, uvPitch, 0, 0 };
                uint* offsets = stackalloc uint[4] { yOffset, uvOffset, 0, 0 };
                var format = KnownPixelFormats.DRM_FORMAT_NV12;
                var resultAddFb = LibDrm.drmModeAddFB2(drmDevice.DeviceFd, (uint)width, (uint)height, format.Fourcc, handles, pitches, offsets, out var fbId, 0);
                if (resultAddFb != 0)
                {
                    Console.WriteLine($"Failed to create framebuffer: {resultAddFb}");
                    return false;
                }

                Console.WriteLine($"Created framebuffer with ID: {fbId}");

                // For NV12 (YUV format), we need to ensure CRTC is enabled first
                // We'll set up the CRTC without a framebuffer (or with a dummy one)
                // Then use the plane to display the NV12 content
                
                // First, check if CRTC is already active
                var crtcInfo = LibDrm.drmModeGetCrtc(drmDevice.DeviceFd, crtcId);
                bool needsCrtcSetup = crtcInfo == null || crtcInfo->BufferId == 0;

                if (needsCrtcSetup)
                {
                    Console.WriteLine("CRTC needs to be initialized, setting up mode...");
                    
                    // Convert managed mode to native mode
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
                    var nameBytes = System.Text.Encoding.UTF8.GetBytes(mode.Name ?? "");
                    for (int i = 0; i < Math.Min(nameBytes.Length, 32); i++)
                    {
                        nativeMode.Name[i] = nameBytes[i];
                    }

                    // Set CRTC with mode but no framebuffer (0 = no framebuffer)
                    uint connectorId = connector.ConnectorId;
                    result = LibDrm.drmModeSetCrtc(drmDevice.DeviceFd, crtcId, 0, 0, 0, &connectorId, 1, &nativeMode);
                    if (result != 0)
                    {
                        Console.WriteLine($"Failed to set CRTC mode: {result}");
                        LibDrm.drmModeRmFB(drmDevice.DeviceFd, fbId);
                        return false;
                    }
                    Console.WriteLine("CRTC mode set successfully");
                }

                if (crtcInfo != null)
                {
                    LibDrm.drmModeFreeCrtc(crtcInfo);
                }

                // Now set the plane with the NV12 framebuffer
                result = LibDrm.drmModeSetPlane(drmDevice.DeviceFd, plane.Id, crtcId, fbId, 0, 0, 0, (uint)width, (uint)height, 0, 0, (uint)width << 16, (uint)height << 16);
                if (result != 0)
                {
                    Console.WriteLine($"Failed to set plane: {result}");
                    LibDrm.drmModeRmFB(drmDevice.DeviceFd, fbId);
                    return false;
                }

                Console.WriteLine("Successfully set CRTC and displayed image");
                return true;
            }
        }
    }
}
