using System;

using Avalonia;
using Avalonia.LinuxFramebuffer.Input.LibInput;

using Microsoft.Extensions.Logging;

using SharpVideo.Avalonia.LinuxFramebuffer.Output;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Utils;

namespace SharpVideo.AvaloniaMpDemo;

internal class Program
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const int FrameCount = 300; // 10 seconds at 30fps

    private static readonly ILoggerFactory LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory
        .Create(builder => builder.AddConsole()
#if DEBUG
                .SetMinimumLevel(LogLevel.Trace)
#else
      .SetMinimumLevel(LogLevel.Warning)
#endif
        );

    private static ILogger Logger = LoggerFactory.CreateLogger<Program>();

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var drmDevice = DrmUtils.OpenDrmDevice(Logger);
        drmDevice.EnableDrmCapabilities(Logger);

        var allocator = DmaBuffersAllocator.Create();
        var buffersManager = new DrmBufferManager(
            drmDevice,
            allocator,
            [KnownPixelFormats.DRM_FORMAT_ARGB8888, KnownPixelFormats.DRM_FORMAT_NV12],
            LoggerFactory.CreateLogger<DrmBufferManager>());

        var presenter = DrmPresenter.Create(
            drmDevice,
            Width,
            Height,
            buffersManager,
            KnownPixelFormats.DRM_FORMAT_ARGB8888, // Primary plane format
            KnownPixelFormats.DRM_FORMAT_NV12, // Overlay plane format
            Logger);

        var drmOutput = new SharpVideoDrmOutput(drmDevice, presenter.PrimaryPlanePresenter);

        BuildAvaloniaApp()
            .StartLinuxDirect(args, drmOutput, new LibInputBackend());
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}