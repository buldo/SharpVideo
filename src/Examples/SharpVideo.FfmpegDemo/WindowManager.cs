using System.Numerics;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Logging;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SharpVideo.Decoding;

namespace SharpVideo.FfmpegDemo;

/// <summary>
/// Manages the rendering window and OpenGL context
/// </summary>
internal unsafe class WindowManager : IDisposable
{
    private readonly ILogger<WindowManager> _logger;
    private readonly BaseDecoder _decoder;
    private readonly ILoggerFactory _loggerFactory;

    private IWindow? _window;
    private GL? _gl;
    private FfmpegGlRenderer? _glRenderer;
    private ImGuiContextPtr _imguiContext;

    private FfmpegDecodedFrame? _currentFrame;
    private readonly object _frameLock = new();

    // Store last frame info for statistics display
    private int _lastFrameWidth;
    private int _lastFrameHeight;
    private int _lastFrameFormat;
    private long _lastFramePts;
    private bool _lastFrameIsKey;

    private bool _disposed;

    public WindowManager(BaseDecoder decoder, ILoggerFactory loggerFactory)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<WindowManager>();
    }

    public void CreateAndRun()
    {
        _logger.LogInformation("Creating display window");

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
    }

    private void OnLoad()
    {
        try
        {
            _logger.LogInformation("Window loaded, initializing OpenGL renderer");

            _gl = _window!.CreateOpenGL();
            _glRenderer = new FfmpegGlRenderer(_gl, _loggerFactory.CreateLogger<FfmpegGlRenderer>());

            InitializeImGui();

            _logger.LogInformation("Display window ready");

            // Start frame fetching task
            Task.Run(FrameFetchingLoop);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error in OnLoad");
            _window?.Close();
        }
    }

    private void InitializeImGui()
    {
        _logger.LogDebug("Creating ImGui context...");
        _imguiContext = ImGui.CreateContext();
        ImGui.SetCurrentContext(_imguiContext);

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.DockingEnable;
        io.DisplaySize = new Vector2(_window!.Size.X, _window.Size.Y);

        _logger.LogDebug("Initializing ImGui OpenGL3 backend...");
        Hexa.NET.ImGui.Backends.OpenGL3.ImGuiImplOpenGL3.SetCurrentContext(_imguiContext);
        if (!Hexa.NET.ImGui.Backends.OpenGL3.ImGuiImplOpenGL3.Init("#version 330"))
        {
            throw new Exception("Failed to initialize ImGui OpenGL3 backend");
        }

        _logger.LogDebug("ImGui initialized successfully");
    }

    private async void FrameFetchingLoop()
    {
        _logger.LogInformation("Starting frame fetching loop");
        int frameCount = 0;

        try
        {
            while (!_disposed)
            {
                _logger.LogTrace("Waiting for decoded frame #{Count}", frameCount + 1);

                // Wait for a decoded frame from the decoder
                var decodedFrame = _decoder.WaitForDecodedFrames();

                _logger.LogTrace("Received decoded frame #{Count}", frameCount + 1);

                if (decodedFrame is FfmpegDecodedFrame ffmpegFrame)
                {
                    FfmpegDecodedFrame? frameToReturn = null;

                    lock (_frameLock)
                    {
                        if (_currentFrame != null)
                        {
                            _logger.LogWarning("Overwriting unrendered frame! Frame #{Count} - returning old frame", frameCount);
                            frameToReturn = _currentFrame;
                        }
                        _currentFrame = ffmpegFrame;
                        _logger.LogTrace("Frame #{Count} ready for rendering", frameCount + 1);
                    }

                    // Return old frame if we had one
                    if (frameToReturn != null)
                    {
                        _logger.LogTrace("Returning overwritten frame to decoder");
                        _decoder.ReuseDecodedFrame(frameToReturn);
                    }

                    frameCount++;

                    if (frameCount % 30 == 0)
                    {
                        _logger.LogDebug("Fetched {Count} frames so far", frameCount);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in frame fetching loop after {Count} frames", frameCount);
        }
        finally
        {
            _logger.LogInformation("Exiting frame fetching loop, fetched {Count} frames", frameCount);
        }
    }

    private void OnRender(double deltaTime)
    {
        if (_gl == null || _glRenderer == null)
        {
            return;
        }

        // Clear background
        _gl.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        // Upload and render video frame if available
        FfmpegDecodedFrame? frameToReturn = null;
        lock (_frameLock)
        {
            if (_currentFrame != null)
            {
                _logger.LogTrace("Uploading frame to OpenGL");
                _glRenderer.UploadFrame(_currentFrame);

                // Save frame info for statistics
                var frame = _currentFrame.Frame;
                _lastFrameWidth = frame->width;
                _lastFrameHeight = frame->height;
                _lastFrameFormat = frame->format;
                _lastFramePts = frame->pts;
                _lastFrameIsKey = (frame->flags & FFmpeg.AutoGen.ffmpeg.AV_FRAME_FLAG_KEY) != 0;

                frameToReturn = _currentFrame;
                _currentFrame = null;
                _logger.LogTrace("Frame marked for return to decoder");
            }
        }

        _glRenderer.Render();

        // Return frame to decoder after OpenGL has copied the data
        if (frameToReturn != null)
        {
            _logger.LogTrace("Returning frame to decoder");
            _decoder.ReuseDecodedFrame(frameToReturn);
            _logger.LogTrace("Frame returned to decoder");
        }

        // Render ImGui overlay
        RenderImGui(deltaTime);
    }

    private void RenderImGui(double deltaTime)
    {
        var io = ImGui.GetIO();
        io.DeltaTime = (float)deltaTime;
        io.DisplaySize = new Vector2(_window!.Size.X, _window.Size.Y);

        Hexa.NET.ImGui.Backends.OpenGL3.ImGuiImplOpenGL3.NewFrame();
        ImGui.NewFrame();

        // Stats window
        ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(300, 200), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Decoder Statistics"))
        {
            if (_lastFrameWidth > 0)
            {
                ImGui.Text($"Resolution: {_lastFrameWidth}x{_lastFrameHeight}");
                ImGui.Text($"Format: {_lastFrameFormat}");
                ImGui.Text($"PTS: {_lastFramePts}");
                ImGui.Text($"Key Frame: {_lastFrameIsKey}");
            }
            else
            {
                ImGui.Text("Waiting for frames...");
            }
        }
        ImGui.End();

        ImGui.Render();
        var drawData = ImGui.GetDrawData();
        Hexa.NET.ImGui.Backends.OpenGL3.ImGuiImplOpenGL3.RenderDrawData(drawData);
    }

    private void OnClosing()
    {
        _logger.LogInformation("Window closing");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _logger.LogInformation("Cleaning up window resources");

        // Return current frame if we have one
        lock (_frameLock)
        {
            if (_currentFrame != null)
            {
                _decoder.ReuseDecodedFrame(_currentFrame);
                _currentFrame = null;
            }
        }

        // Cleanup ImGui before GL renderer
        try
        {
            Hexa.NET.ImGui.Backends.OpenGL3.ImGuiImplOpenGL3.Shutdown();
            ImGui.DestroyContext(_imguiContext);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error during ImGui cleanup");
        }

        _glRenderer?.Dispose();
        _gl?.Dispose();

        _logger.LogInformation("Window cleanup complete");
    }
}
