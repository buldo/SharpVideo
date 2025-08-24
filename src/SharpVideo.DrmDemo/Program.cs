using SharpVideo.Drm;

namespace SharpVideo.DrmDemo
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

            if(drmDevice == null)
            {
                Console.WriteLine("No DRM devices could be opened.");
                return;
            }

            var resources = drmDevice.GetResources();
            Console.WriteLine($"Width: {resources?.MinWidth} - {resources?.MaxWidth}");
            Console.WriteLine($"Height: {resources?.MinHeight} - {resources?.MaxHeight}");
            Console.WriteLine($"Framebuffers: {string.Join(", ", resources?.FrameBuffers ?? Array.Empty<uint>())}");
            Console.WriteLine($"CRTCs: {string.Join(", ", resources?.Crtcs ?? Array.Empty<uint>())}");
            Console.WriteLine($"Connectors: {string.Join(", ", resources?.Connectors ?? Array.Empty<uint>())}");
            Console.WriteLine($"Encoders: {string.Join(", ", resources?.Encoders ?? Array.Empty<uint>())}");
        }
    }
}
