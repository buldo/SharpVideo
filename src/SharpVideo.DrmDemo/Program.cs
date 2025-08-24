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

            foreach (var connector in resources.Connectors)
            {
                Console.WriteLine($"Connector {connector.ConnectorId}. ConnectorType {connector.ConnectorType}, MmWidth {connector.MmWidth}, MmHeight {connector.MmHeight}");
                Console.WriteLine("Modes:");
                foreach (var mode in connector.Modes)
                {
                    Console.WriteLine($"  {mode.Name}: {mode.HDisplay}x{mode.VDisplay} @ {mode.VRefresh}Hz. Flags {mode.Flags}, Type {mode.Type}");
                }

                Console.WriteLine("Props:");
                foreach (var prop in connector.Props)
                {
                    Console.WriteLine($"  Prop {prop.Id}: {prop.Name}");
                }
            }

            foreach (var encoder in resources.Encoders)
            {
                Console.WriteLine($"Encoder {encoder.EncoderId}: EncoderType {encoder.EncoderType}, Crtc {encoder.CrtcId}, PossibleClones {encoder.PossibleClones}, PossibleCrtcs {encoder.PossibleCrtcs}");
            }
        }
    }
}
