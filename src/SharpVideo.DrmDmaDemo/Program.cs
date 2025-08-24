using SharpVideo.DmaBuffers;
using SharpVideo.Drm;

namespace SharpVideo.DrmDmaDemo
{
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

            if (!DmaBuffersAllocator.TryCreate(out var allocator))
            {
                Console.WriteLine("Failed to create DMA buffers allocator.");
                return;
            }

            // Use the allocator to allocate DMA buffers as needed
        }
    }
}
