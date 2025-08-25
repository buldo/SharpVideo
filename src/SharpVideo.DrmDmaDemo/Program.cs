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
            const int bpp = 2; // YUV422 has 2 bytes per pixel
            ulong bufferSize = (ulong)(width * height * bpp);

            var dmaBuf = allocator.Allocate(bufferSize);
            if (dmaBuf == null)
            {
                Console.WriteLine("Failed to allocate DMA buffer.");
                return;
            }

            Console.WriteLine($"Allocated DMA buffer of size {dmaBuf.Size} with fd {dmaBuf.Fd}");

            // Map the buffer to fill it
            var map = Libc.mmap(IntPtr.Zero, (IntPtr)dmaBuf.Size,
                Libc.ProtFlags.PROT_READ | Libc.ProtFlags.PROT_WRITE,
                Libc.MapFlags.MAP_SHARED, dmaBuf.Fd, 0);

            if (map == Libc.MAP_FAILED)
            {
                Console.WriteLine("Failed to mmap DMA buffer.");
                dmaBuf.Dispose();
                return;
            }

            Console.WriteLine($"DMA buffer mapped at {map:X}");

            // Fill with test pattern
            unsafe
            {
                TestPattern.FillYuv422((byte*)map, width, height);
            }

            Console.WriteLine("Filled DMA buffer with YUV422 test pattern.");

            // Sync the buffer to ensure writes are committed
            Libc.msync(map, (IntPtr)dmaBuf.Size, Libc.MsyncFlags.MS_SYNC);
            Console.WriteLine("Synced DMA buffer.");

            // Make the buffer read-only to prevent accidental modification
            if (Libc.mprotect(map, (IntPtr)dmaBuf.Size, Libc.ProtFlags.PROT_READ) != 0)
            {
                Console.WriteLine("Warning: Failed to make buffer read-only, continuing anyway.");
            }
            else
            {
                Console.WriteLine("Made buffer read-only to prevent modification.");
            }

            // Present the buffer on the display
            if (PresentBuffer(drmDevice, dmaBuf, width, height))
            {
                Console.WriteLine("Successfully presented buffer on display.");

                // Keep the display active and the buffer mapped for the entire duration
                Console.WriteLine("Displaying pattern for 10 seconds...");
                Thread.Sleep(10000);

                // Now unmap the buffer
                Libc.munmap(map, (IntPtr)dmaBuf.Size);
                Console.WriteLine("Unmapped DMA buffer.");
            }
            else
            {
                Console.WriteLine("Failed to present buffer on display.");
                // Unmap on failure
                Libc.munmap(map, (IntPtr)dmaBuf.Size);
            }

            // The buffer will be disposed automatically when the allocator is disposed
            // or you can dispose it manually if you are done with it.
            // dmaBuf.Dispose();
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

                // Create framebuffer
                uint pitch = (uint)(width * 2); // YUV422 has 2 bytes per pixel
                result = LibDrm.drmModeAddFB(drmDevice.DeviceFd, (uint)width, (uint)height, 16, 16, pitch, handle, out uint fbId);
                if (result != 0)
                {
                    Console.WriteLine($"Failed to create framebuffer: {result}");
                    return false;
                }

                Console.WriteLine($"Created framebuffer with ID: {fbId}");

                // Convert managed mode to native mode using normal initialization
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

                // Set CRTC
                uint connectorId = connector.ConnectorId;
                result = LibDrm.drmModeSetCrtc(drmDevice.DeviceFd, crtcId, fbId, 0, 0, &connectorId, 1, &nativeMode);
                if (result != 0)
                {
                    Console.WriteLine($"Failed to set CRTC: {result}");
                    LibDrm.drmModeRmFB(drmDevice.DeviceFd, fbId);
                    return false;
                }

                Console.WriteLine("Successfully set CRTC and displayed image");
                return true;
            }
        }
    }
}
