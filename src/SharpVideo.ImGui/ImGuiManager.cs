using System.Diagnostics;
using System.Runtime.Versioning;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Microsoft.Extensions.Logging;
using SharpVideo.Utils;

namespace SharpVideo.ImGui;

/// <summary>
/// High-level manager for ImGui integration with DRM/KMS rendering.
/// Handles ImGui context lifecycle, rendering, and input processing.
/// </summary>
/// <remarks>
/// This class manages:
/// - ImGui context creation and configuration
/// - OpenGL ES backend initialization
/// - Frame timing and rendering coordination
/// - Optional input handling via libinput
/// - Integration with DRM plane presenters
/// 
/// The library is designed to work with multi-plane DRM setups where
/// ImGui can be rendered on a dedicated overlay plane with transparency.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class ImGuiManager : IDisposable
{
    private readonly ImGuiDrmConfiguration _config;
    private readonly ILogger? _logger;
    private readonly ImGuiDrmRenderer _renderer;
    private readonly ImGuiInputAdapter? _inputAdapter;
    private readonly ImGuiContextPtr _imguiContext;
    
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private TimeSpan _lastFrameTime;
    private bool _disposed;

    /// <summary>
    /// Creates a new ImGui manager with the specified configuration.
    /// </summary>
    /// <param name="config">DRM rendering configuration</param>
    /// <param name="inputManager">Optional input manager for mouse/keyboard support</param>
    /// <param name="logger">Optional logger</param>
    /// <exception cref="ArgumentNullException">If config is null</exception>
    /// <exception cref="ArgumentException">If config is invalid</exception>
    /// <exception cref="InvalidOperationException">If initialization fails</exception>
    public ImGuiManager(
        ImGuiDrmConfiguration config,
        InputManager? inputManager = null,
        ILogger? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _config.Validate();
        _logger = logger;

        _logger?.LogInformation("Initializing ImGui manager for DRM/KMS rendering");

        try
        {
            // Create ImGui context
            _imguiContext = Hexa.NET.ImGui.ImGui.CreateContext();
            Hexa.NET.ImGui.ImGui.SetCurrentContext(_imguiContext);
            _logger?.LogDebug("ImGui context created");

            // Configure ImGui
            var io = Hexa.NET.ImGui.ImGui.GetIO();
            io.ConfigFlags |= config.ConfigFlags;
            io.MouseDrawCursor = config.DrawCursor;
            io.DisplaySize = new System.Numerics.Vector2(config.Width, config.Height);

            // Apply UI scale
            if (config.UiScale != 1.0f)
            {
                var style = Hexa.NET.ImGui.ImGui.GetStyle();
                style.ScaleAllSizes(config.UiScale);
            }

            _logger?.LogDebug("ImGui configured (size: {Width}x{Height}, scale: {Scale})", 
                config.Width, config.Height, config.UiScale);

            // Initialize renderer
            _renderer = new ImGuiDrmRenderer(config, logger);

            // Initialize ImGui OpenGL3 backend
            ImGuiImplOpenGL3.SetCurrentContext(_imguiContext);
            if (!ImGuiImplOpenGL3.Init(config.GlslVersion))
            {
                throw new InvalidOperationException("Failed to initialize ImGui OpenGL3 backend");
            }

            _logger?.LogDebug("ImGui OpenGL3 backend initialized (GLSL: {Version})", config.GlslVersion);

            // Initialize input if enabled
            if (config.EnableInput)
            {
                if (inputManager == null)
                {
                    throw new ArgumentException(
                        "InputManager must be provided when EnableInput is true", 
                        nameof(inputManager));
                }

                _inputAdapter = new ImGuiInputAdapter(inputManager, io);
                _logger?.LogDebug("Input adapter initialized");
            }

            _logger?.LogInformation("ImGui manager initialized successfully");
        }
        catch
        {
            // Cleanup on failure
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the ImGui IO pointer for advanced configuration.
    /// </summary>
    public ImGuiIOPtr IO => Hexa.NET.ImGui.ImGui.GetIO();

    /// <summary>
    /// Gets the current frame delta time in seconds.
    /// </summary>
    public float DeltaTime { get; private set; }

    /// <summary>
    /// Begins a new ImGui frame.
    /// Call this before any ImGui drawing commands.
    /// </summary>
    public void BeginFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Update timing
        var currentTime = _stopwatch.Elapsed;
        DeltaTime = (float)(currentTime - _lastFrameTime).TotalSeconds;
        _lastFrameTime = currentTime;

        var io = IO;
        io.DeltaTime = DeltaTime > 0 ? DeltaTime : 1.0f / 60.0f;

        // Update input state
        _inputAdapter?.UpdateInput();

        // Begin ImGui frame
        ImGuiImplOpenGL3.NewFrame();
        Hexa.NET.ImGui.ImGui.NewFrame();
    }

    /// <summary>
    /// Ends the current ImGui frame and renders to the back buffer.
    /// Does NOT swap buffers - call SwapBuffers() to present.
    /// </summary>
    public void EndFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Hexa.NET.ImGui.ImGui.Render();
        var drawData = Hexa.NET.ImGui.ImGui.GetDrawData();
        _renderer.RenderDrawData(drawData);
    }

    /// <summary>
    /// Swaps the rendering buffers to commit the frame.
    /// Returns true if successful, false if the frame should be dropped.
    /// </summary>
    /// <returns>True if buffers were swapped successfully</returns>
    public bool SwapBuffers()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _renderer.SwapBuffers();
    }

    /// <summary>
    /// Renders a complete frame using the provided render delegate.
    /// This is a convenience method that calls BeginFrame, renderDelegate, EndFrame, and SwapBuffers.
    /// </summary>
    /// <param name="renderDelegate">Delegate to invoke for rendering ImGui content</param>
    /// <returns>True if the frame was swapped successfully</returns>
    public bool RenderFrame(ImGuiRenderDelegate renderDelegate)
    {
        ArgumentNullException.ThrowIfNull(renderDelegate);
        
        BeginFrame();
        renderDelegate(DeltaTime);
        EndFrame();
        return SwapBuffers();
    }

    /// <summary>
    /// Renders a complete frame using the provided drawable.
    /// This is a convenience method that calls BeginFrame, drawable.Draw, EndFrame, and SwapBuffers.
    /// </summary>
    /// <param name="drawable">Drawable to invoke for rendering ImGui content</param>
    /// <returns>True if the frame was swapped successfully</returns>
    public bool RenderFrame(IImGuiDrawable drawable)
    {
        ArgumentNullException.ThrowIfNull(drawable);
        
        BeginFrame();
        drawable.Draw(DeltaTime);
        EndFrame();
        return SwapBuffers();
    }

    /// <summary>
    /// Performs a warmup frame to initialize the display pipeline.
    /// Call this once after initialization before starting the main render loop.
    /// </summary>
    /// <param name="renderDelegate">Optional delegate to render content during warmup</param>
    /// <returns>True if warmup frame was swapped successfully</returns>
    public bool WarmupFrame(ImGuiRenderDelegate? renderDelegate = null)
    {
        _logger?.LogDebug("Rendering warmup frame");
        
        BeginFrame();
        renderDelegate?.Invoke(DeltaTime);
        EndFrame();
        
        return SwapBuffers();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _logger?.LogDebug("Disposing ImGui manager");

        // Shutdown ImGui backend
        try
        {
            ImGuiImplOpenGL3.Shutdown();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error shutting down ImGui OpenGL3 backend");
        }

        // Destroy ImGui context
        try
        {
            // Check if context is valid before destroying
            Hexa.NET.ImGui.ImGui.DestroyContext(_imguiContext);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error destroying ImGui context");
        }

        // Dispose renderer
        _renderer?.Dispose();

        _disposed = true;
        _logger?.LogDebug("ImGui manager disposed");
    }
}
