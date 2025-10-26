using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Utils;

namespace SharpVideo.MultiPlaneExample
{
    [SupportedOSPlatform("linux")]
    internal class Program
    {
        private const int Width = 1920;
        private const int Height = 1080;

        private static readonly ILoggerFactory LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory
            .Create(builder => builder.AddConsole()
#if DEBUG
                    .SetMinimumLevel(LogLevel.Trace)
#else
        .SetMinimumLevel(LogLevel.Warning)
#endif
            );

        private static readonly ILogger Logger = LoggerFactory.CreateLogger<Program>();

        static void Main(string[] args)
        {
            var drmDevice = DrmUtils.OpenDrmDevice(Logger);
            drmDevice.EnableDrmCapabilities(Logger);

            var allocator = DmaBuffersAllocator.Create();
            var buffersManager = new DrmBufferManager(
                drmDevice,
                allocator,
                [KnownPixelFormats.DRM_FORMAT_ARGB8888, KnownPixelFormats.DRM_FORMAT_NV12],
                LoggerFactory.CreateLogger<DrmBufferManager>());
            var presenter = DrmPresenter.Create(drmDevice, Width, Height, buffersManager, Logger);
            
        }
    }
}
