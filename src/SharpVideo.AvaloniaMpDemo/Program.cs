using System;

using Avalonia;
using Avalonia.LinuxFramebuffer.Input.LibInput;
using SharpVideo.Avalonia.LinuxFramebuffer.Output;

namespace SharpVideo.AvaloniaMpDemo
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            BuildAvaloniaApp()
                .StartLinuxDirect(args, new SharpVideoDrmOutput(), new LibInputBackend());
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
}
