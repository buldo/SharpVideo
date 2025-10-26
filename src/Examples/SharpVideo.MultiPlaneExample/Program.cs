using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

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
            Console.WriteLine("Hello, World!");
        }
    }
}
