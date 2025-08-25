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

            // Use the allocator to allocate DMA buffers as needed
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

            // Unmap the buffer
            Libc.munmap(map, (IntPtr)dmaBuf.Size);
            Console.WriteLine("Unmapped DMA buffer.");

            // The buffer will be disposed automatically when the allocator is disposed
            // or you can dispose it manually if you are done with it.
            // dmaBuf.Dispose();
        }
    }
}
