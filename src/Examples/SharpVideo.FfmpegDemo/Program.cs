using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SharpVideo.Decoding.Ffmpeg;
using SharpVideo.FfmpegBin;
using SharpVideo.FfmpegDemo.NaluSources;

namespace SharpVideo.FfmpegDemo;

internal class Program
{
    private static readonly ILoggerFactory LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory
        .Create(builder => builder.AddConsole()
                .SetMinimumLevel(LogLevel.Trace)
        );

    private static readonly ILogger Logger = LoggerFactory.CreateLogger<Program>();

    static async Task Main(string[] args)
    {
        Logger.LogInformation("FFmpeg H.264 Decoder Demo with OpenGL Display");

        var ffmpegPath = FfmpegLoader.Load(Logger);
        if (ffmpegPath != null)
        {
            ffmpeg.RootPath = ffmpegPath;
        }

        Logger.LogInformation("FFmpeg version: {Version}", ffmpeg.av_version_info());

        // Check if video file exists
        var videoFilePath = "test_video.h264";

        if (!File.Exists(videoFilePath))
        {
            Logger.LogError("Video file not found: {FilePath}", videoFilePath);
            Logger.LogInformation("Please place a test_video.h264 file in the application directory");
            return;
        }

        Logger.LogInformation("Video file found: {FilePath}", videoFilePath);

        // Initialize decoder
        Logger.LogInformation("=== Initializing Decoder ===");
        using var decoder = FfmpegH264Decoder.Create(LoggerFactory);
        Logger.LogInformation("Decoder initialized successfully");

        // Start decoder
        decoder.Start();
        Logger.LogInformation("Decoder started");

        // Open video file and create NALU source
        Logger.LogInformation("Opening video file: {FilePath}", videoFilePath);
        var stream = File.OpenRead(videoFilePath);
        await using var naluSource = new StreamNaluSource(
            stream,
            LoggerFactory.CreateLogger<StreamNaluSource>());

        await naluSource.StartAsync();
        Logger.LogInformation("NALU source started");

        // Start NALU feeding service
        using var naluFeeder = new NaluFeedingService(
            naluSource,
            decoder,
            LoggerFactory.CreateLogger<NaluFeedingService>());

        naluFeeder.Start();
        Logger.LogInformation("NALU feeding service started");

        // Create and run window (blocks until window is closed)
        Logger.LogInformation("=== Creating Display Window ===");
        using var windowManager = new WindowManager(decoder, LoggerFactory);
        windowManager.CreateAndRun();

        // Cleanup
        Logger.LogInformation("=== Cleaning Up ===");
        await naluFeeder.StopAsync();
        decoder.Stop();
        await naluSource.StopAsync();

        Logger.LogInformation("Cleanup complete");
    }
}