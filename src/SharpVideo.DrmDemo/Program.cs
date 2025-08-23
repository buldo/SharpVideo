using SharpVideo.Drm;

namespace SharpVideo.DrmDemo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            DrmDevice.Open("/dev/dri/card0");
            Console.WriteLine("Hello, World!");
        }
    }
}
