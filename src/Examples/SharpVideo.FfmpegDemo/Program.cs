using FFmpeg.AutoGen;
using SharpVideo.H264;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SharpVideo.FfmpegDemo.Models;
using SharpVideo.FfmpegDemo.NaluSources;
using Hexa.NET.ImGui;
using SharpVideo.Decoding.Ffmpeg;
using SharpVideo.FfmpegBin;

namespace SharpVideo.FfmpegDemo;

internal class Program
{
    private static readonly ILoggerFactory LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory
        .Create(builder => builder.AddConsole()
                .SetMinimumLevel(LogLevel.Debug)
        );

    private static readonly ILogger Logger = LoggerFactory.CreateLogger<Program>();

    private static IWindow? _window;
    private static GL? _gl;
    private static FfmpegGlRenderer? _glRenderer;
    private static FfmpegH264Decoder? _decoder;
    private static StreamNaluSource? _naluSource;

    private static FfmpegVideoFrame? _latestFrame;
    private static readonly object _frameLock = new();
    private static bool _hasNewFrame;
    private static ImGuiContextPtr _imguiContext;

    private static string? _videoFilePath;

    static async Task Main(string[] args)
    {
        Logger.LogInformation("FFmpeg H.264 Decoder Demo with OpenGL Display");

        var ffmpegPath = FfmpegLoader.Load(Logger);
        if (ffmpegPath != null)
        {
            ffmpeg.RootPath = ffmpegPath;
        }

        Logger.LogInformation("FFmpeg version: {Version}", ffmpeg.av_version_info());

        // Check if video file exists before creating window
        _videoFilePath = "test_video.h264";

        if (!File.Exists(_videoFilePath))
        {
            Logger.LogError("Video file not found: {FilePath}", _videoFilePath);
            Logger.LogInformation("Please place a test_video.h264 file in the application directory");
            return;
        }

        Logger.LogInformation("Video file found: {FilePath}", _videoFilePath);
        Logger.LogInformation("=== Creating Display Window ===");

        // Create window for display
        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(1920, 1080),
            Title = "FFmpeg H.264 Decoder Demo",
            VSync = true,
            API = new GraphicsAPI(
                ContextAPI.OpenGL,
                new APIVersion(3, 3))
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Closing += OnClosing;

        _window.Run();

        // Cleanup
        await CleanupAsync();
    }

    private static async void OnLoad()
    {
        try
        {
            Logger.LogInformation("Window loaded, initializing OpenGL renderer");

            // Initialize OpenGL renderer only
            Logger.LogDebug("Creating OpenGL context...");
            _gl = _window!.CreateOpenGL();
            Logger.LogDebug("OpenGL context created");

            _glRenderer = new FfmpegGlRenderer(_gl, LoggerFactory.CreateLogger<FfmpegGlRenderer>());
            Logger.LogDebug("GL renderer created");

            // Initialize ImGui
            Logger.LogDebug("Creating ImGui context...");
            _imguiContext = Hexa.NET.ImGui.ImGui.CreateContext();
            Hexa.NET.ImGui.ImGui.SetCurrentContext(_imguiContext);

            var io = Hexa.NET.ImGui.ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.DockingEnable;
            io.DisplaySize = new System.Numerics.Vector2(_window.Size.X, _window.Size.Y);

            Logger.LogDebug("Initializing ImGui OpenGL3 backend...");
            Hexa.NET.ImGui.Backends.OpenGL3.ImGuiImplOpenGL3.SetCurrentContext(_imguiContext);
            if (!Hexa.NET.ImGui.Backends.OpenGL3.ImGuiImplOpenGL3.Init("#version 330"))
            {
                Logger.LogError("Failed to initialize ImGui OpenGL3 backend");
                _window.Close();
                return;
            }
            Logger.LogDebug("ImGui initialized successfully");

            Logger.LogInformation("Display window ready");

            // NOW initialize and start decoder after window is ready
            Logger.LogInformation("=== Initializing Decoder ===");

            var config = new FfmpegDecoderConfiguration
            {
                Width = 1920,
                Height = 1080,
                ThreadCount = 0
            };

            _decoder = FfmpegH264Decoder.Create(LoggerFactory);

            Logger.LogInformation("Decoder initialized successfully");

            // Start decoding from file
            Logger.LogInformation("Opening video file: {FilePath}", _videoFilePath);

            var stream = File.OpenRead(_videoFilePath!);
            _naluSource = new StreamNaluSource(
                stream,
                LoggerFactory.CreateLogger<StreamNaluSource>());

            await _naluSource.StartAsync();

            Logger.LogInformation("Starting decoder");
            _decoder.Start();
            Logger.LogInformation("Decoder started, frames will be rendered as they are decoded");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Critical error in OnLoad");
            _window?.Close();
        }
    }

    private static void OnFrameDecoded(FfmpegVideoFrame frame)
    {
        lock (_frameLock)
        {
            _latestFrame = frame;
            _hasNewFrame = true;
        }
    }

    private static unsafe void OnRender(double deltaTime)
    {
        if (_gl == null || _glRenderer == null)
        {
            return;
        }

        // Clear background
        _gl.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        // Upload and render video frame if available
        FfmpegVideoFrame? frameToRender = null;
        lock (_frameLock)
        {
            if (_hasNewFrame && _latestFrame != null)
            {
                frameToRender = _latestFrame;
                _hasNewFrame = false;
            }
        }

        if (frameToRender != null)
        {
            _glRenderer.UploadFrame(frameToRender);
        }

        _glRenderer.Render();

        // Render ImGui overlay
        RenderImGui(deltaTime);
    }

    private static unsafe void RenderImGui(double deltaTime)
    {
        var io = Hexa.NET.ImGui.ImGui.GetIO();
        io.DeltaTime = (float)deltaTime;
        io.DisplaySize = new System.Numerics.Vector2(_window!.Size.X, _window.Size.Y);

        Hexa.NET.ImGui.Backends.OpenGL3.ImGuiImplOpenGL3.NewFrame();
        Hexa.NET.ImGui.ImGui.NewFrame();

        // Stats window
        Hexa.NET.ImGui.ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.FirstUseEver);
        Hexa.NET.ImGui.ImGui.SetNextWindowSize(new System.Numerics.Vector2(300, 200), ImGuiCond.FirstUseEver);

        if (Hexa.NET.ImGui.ImGui.Begin("Decoder Statistics"))
        {
            if (_decoder != null)
            {
                //var stats = _decoder.Statistics;
                //Hexa.NET.ImGui.ImGui.Text($"Frames Decoded: {stats.FramesDecoded}");
                //Hexa.NET.ImGui.ImGui.Text($"Packets Sent: {stats.PacketsSent}");
                //Hexa.NET.ImGui.ImGui.Text($"Elapsed Time: {stats.DecodeElapsed.TotalSeconds:F2}s");
                //Hexa.NET.ImGui.ImGui.Text($"Average FPS: {stats.AverageFps:F2}");

                Hexa.NET.ImGui.ImGui.Separator();

                lock (_frameLock)
                {
                    if (_latestFrame != null)
                    {
                        Hexa.NET.ImGui.ImGui.Text($"Resolution: {_latestFrame.Width}x{_latestFrame.Height}");
                        Hexa.NET.ImGui.ImGui.Text($"Format: {_latestFrame.PixelFormat}");
                        Hexa.NET.ImGui.ImGui.Text($"PTS: {_latestFrame.Pts}");
                        Hexa.NET.ImGui.ImGui.Text($"Key Frame: {_latestFrame.IsKeyFrame}");
                    }
                }
            }
        }
        Hexa.NET.ImGui.ImGui.End();

        Hexa.NET.ImGui.ImGui.Render();
        var drawData = Hexa.NET.ImGui.ImGui.GetDrawData();
        Hexa.NET.ImGui.Backends.OpenGL3.ImGuiImplOpenGL3.RenderDrawData(drawData);
    }

    private static void OnClosing()
    {
        Logger.LogInformation("Window closing");
    }

    private static async Task CleanupAsync()
    {
        Logger.LogInformation("Cleaning up resources");

        if (_decoder != null)
        {
            _decoder.Dispose();
        }

        if (_naluSource != null)
        {
            await _naluSource.DisposeAsync();
        }

        // Cleanup ImGui before GL renderer (ImGui uses OpenGL resources)
        try
        {
            Hexa.NET.ImGui.Backends.OpenGL3.ImGuiImplOpenGL3.Shutdown();
            Hexa.NET.ImGui.ImGui.DestroyContext(_imguiContext);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Error during ImGui cleanup");
        }

        // Cleanup GL renderer before disposing GL context
        _glRenderer?.Dispose();

        // Finally dispose GL context
        _gl?.Dispose();

        Logger.LogInformation("Cleanup complete");
    }
}