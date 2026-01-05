using System.Runtime.Versioning;

using Hexa.NET.ImGui;

using Microsoft.Extensions.Logging;

using SharpVideo.Decoding.OhdDemo.Configuration;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.ImGui;
using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.C;
using SharpVideo.Utils;

namespace SharpVideo.Decoding.OhdDemo.ImguiOsd;

/// <summary>
/// Encapsulates DRM/KMS resource initialization and management.
/// Handles device setup, plane configuration, and ImGui integration.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class DrmRenderingContext : IDisposable
{
    private readonly ILogger _logger;
    private readonly DrmBufferManager _drmBufferManager;
    private readonly bool _ownsBufferManager;

    private GbmDevice? _gbmDevice;
    private DrmPresenter? _presenter;
    private InputManager? _inputManager;
    private ImGuiManager? _imguiManager;

    private bool _disposed;
    private bool _exitRequested;

    /// <summary>
    /// Gets the DRM buffer manager for video plane allocation.
    /// </summary>
    public DrmBufferManager BufferManager => _drmBufferManager;

    /// <summary>
    /// Gets the DRM presenter for plane management.
    /// </summary>
    public DrmPresenter Presenter =>
        _presenter ?? throw new InvalidOperationException("Context not initialized");

    /// <summary>
    /// Gets the overlay plane presenter for video rendering.
    /// </summary>
    public DrmPlaneLastDmaBufferPresenter OverlayPresenter =>
        _presenter?.OverlayPlanePresenter ?? throw new InvalidOperationException("Context not initialized");

    /// <summary>
    /// Gets the ImGui manager for OSD rendering.
    /// </summary>
    public ImGuiManager ImGuiManager =>
        _imguiManager ?? throw new InvalidOperationException("Context not initialized");

    /// <summary>
    /// Gets the actual display width after initialization.
    /// </summary>
    public uint DisplayWidth { get; private set; }

    /// <summary>
    /// Gets the actual display height after initialization.
    /// </summary>
    public uint DisplayHeight { get; private set; }

    /// <summary>
    /// Gets whether exit has been requested (e.g., ESC key pressed).
    /// </summary>
    public bool ExitRequested => _exitRequested;

    private DrmRenderingContext(DrmBufferManager drmBufferManager, bool ownsBufferManager, ILogger logger)
    {
        _drmBufferManager = drmBufferManager;
        _ownsBufferManager = ownsBufferManager;
        _logger = logger;
    }

    /// <summary>
    /// Creates and initializes a DRM rendering context using an existing buffer manager.
    /// </summary>
    /// <param name="videoPixelFormat">Pixel format for video frames.</param>
    /// <param name="configuration">DRM host configuration.</param>
    /// <param name="drmBufferManager">Existing DRM buffer manager (ownership is NOT transferred).</param>
    /// <param name="loggerFactory">Logger factory.</param>
    public static DrmRenderingContext Create(
        PixelFormat videoPixelFormat,
        DrmHostConfiguration configuration,
        DrmBufferManager drmBufferManager,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(drmBufferManager);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var logger = loggerFactory.CreateLogger<DrmRenderingContext>();
        var context = new DrmRenderingContext(drmBufferManager, ownsBufferManager: false, logger);

        context.Initialize(videoPixelFormat, configuration, loggerFactory);

        return context;
    }

    /// <summary>
    /// Renders a warmup frame to initialize the display.
    /// </summary>
    public bool RenderWarmupFrame(ImGuiRenderDelegate renderCallback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogInformation("Rendering warmup frame...");

        var gbmAtomicPresenter = _presenter?.AsGbmAtomicPresenter();
        if (gbmAtomicPresenter == null)
        {
            _logger.LogWarning("GBM atomic presenter not available for warmup");
            return false;
        }

        if (!_imguiManager!.WarmupFrame(renderCallback))
        {
            return false;
        }

        if (!gbmAtomicPresenter.SubmitFrame())
        {
            return false;
        }

        _logger.LogInformation("Warmup frame submitted");
        return true;
    }

    /// <summary>
    /// Processes input events and updates exit state.
    /// </summary>
    public void ProcessInput()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_inputManager == null)
        {
            return;
        }

        var inputFd = _inputManager.GetFileDescriptor();
        if (inputFd < 0)
        {
            return;
        }

        var pollFd = new PollFd
        {
            fd = inputFd,
            events = PollEvents.POLLIN
        };

        var pollResult = Libc.poll(ref pollFd, 1, 0);
        if (pollResult > 0)
        {
            _inputManager.ProcessEvents();
        }

        // Check for ESC key
        if (_inputManager.IsKeyDown(LinuxInputConstants.KEY_ESC))
        {
            _logger.LogInformation("ESC key pressed, exit requested");
            _exitRequested = true;
        }
    }

    /// <summary>
    /// Renders an OSD frame using ImGui.
    /// </summary>
    /// <returns>True if frame was rendered and submitted successfully.</returns>
    public bool RenderOsdFrame(ImGuiRenderDelegate renderCallback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var gbmAtomicPresenter = _presenter?.AsGbmAtomicPresenter();
        if (gbmAtomicPresenter == null)
        {
            return false;
        }

        if (!_imguiManager!.RenderFrame(renderCallback))
        {
            return false;
        }

        return gbmAtomicPresenter.SubmitFrame();
    }

    private void Initialize(
        PixelFormat videoPixelFormat,
        DrmHostConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        _logger.LogInformation(
            "Initializing DRM rendering context. Video pixel format: {Format}",
            videoPixelFormat.GetName());

        // Get the DRM device from the buffer manager
        var drmDevice = _drmBufferManager.DrmDevice;

        // Create GBM device for ImGui rendering
        _gbmDevice = GbmDevice.CreateFromDrmDevice(drmDevice);
        _logger.LogInformation("Created GBM device for ImGui rendering");

        // Create unified DRM presenter
        _presenter = DrmPresenter.CreateWithGbmAtomicAndDmaOverlay(
            drmDevice,
            configuration.DisplayWidth,
            configuration.DisplayHeight,
            _gbmDevice,
            _drmBufferManager,
            KnownPixelFormats.DRM_FORMAT_ARGB8888,
            videoPixelFormat,
            _logger);

        if (_presenter == null)
        {
            throw new InvalidOperationException("Failed to create DRM presenter");
        }

        // Get actual display dimensions
        DisplayWidth = _presenter.PrimaryPlanePresenter.Width;
        DisplayHeight = _presenter.PrimaryPlanePresenter.Height;

        if (DisplayWidth != configuration.DisplayWidth || DisplayHeight != configuration.DisplayHeight)
        {
            _logger.LogWarning(
                "Display mode differs from requested: requested {ReqWidth}x{ReqHeight}, actual {ActWidth}x{ActHeight}",
                configuration.DisplayWidth, configuration.DisplayHeight, DisplayWidth, DisplayHeight);
        }

        _logger.LogInformation("Created dual-plane DRM presenter ({Width}x{Height})", DisplayWidth, DisplayHeight);

        // Configure z-order
        ConfigurePlaneZOrder();

        // Initialize input manager
        if (configuration.EnableInput)
        {
            _inputManager = new InputManager(
                DisplayWidth,
                DisplayHeight,
                loggerFactory.CreateLogger<InputManager>());
            _logger.LogInformation("Input manager initialized");
        }

        // Initialize ImGui
        InitializeImGui(drmDevice, configuration, loggerFactory);

        _logger.LogInformation("DRM rendering context initialized successfully");
    }

    private void InitializeImGui(DrmDevice drmDevice, DrmHostConfiguration configuration, ILoggerFactory loggerFactory)
    {
        var gbmAtomicPresenter = _presenter!.AsGbmAtomicPresenter();
        if (gbmAtomicPresenter == null)
        {
            throw new InvalidOperationException("Failed to get GBM atomic presenter");
        }

        var imguiConfig = new ImGuiDrmConfiguration
        {
            Width = DisplayWidth,
            Height = DisplayHeight,
            DrmDevice = drmDevice,
            GbmDevice = _gbmDevice!,
            GbmSurfaceHandle = gbmAtomicPresenter.GetNativeGbmSurfaceHandle(),
            PixelFormat = KnownPixelFormats.DRM_FORMAT_ARGB8888,
            ConfigFlags = ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.DockingEnable,
            DrawCursor = true,
            UiScale = configuration.UiScale,
            GlslVersion = "#version 300 es",
            EnableInput = configuration.EnableInput
        };

        _imguiManager = new ImGuiManager(
            imguiConfig,
            _inputManager,
            loggerFactory.CreateLogger<ImGuiManager>());

        _logger.LogInformation("ImGui manager initialized");
    }

    private void ConfigurePlaneZOrder()
    {
        if (_presenter == null)
        {
            return;
        }

        _logger.LogInformation("Configuring plane z-order...");

        var primaryZposRange = _presenter.PrimaryPlane.GetPlaneZPositionRange();
        var overlayZposRange = _presenter.OverlayPlane.GetPlaneZPositionRange();

        if (primaryZposRange.HasValue)
        {
            _logger.LogInformation("Primary plane zpos range: [{Min}, {Max}], current: {Current}",
                primaryZposRange.Value.min, primaryZposRange.Value.max, primaryZposRange.Value.current);
        }
        else
        {
            _logger.LogWarning("Primary plane does not support zpos property");
        }

        if (overlayZposRange.HasValue)
        {
            _logger.LogInformation("Overlay plane zpos range: [{Min}, {Max}], current: {Current}",
                overlayZposRange.Value.min, overlayZposRange.Value.max, overlayZposRange.Value.current);
        }
        else
        {
            _logger.LogWarning("Overlay plane does not support zpos property");
        }

        if (!primaryZposRange.HasValue || !overlayZposRange.HasValue)
        {
            return;
        }

        var primaryZpos = primaryZposRange.Value.max;
        var overlayZpos = overlayZposRange.Value.min;

        _logger.LogInformation(
            "Setting Primary zpos={PrimaryZpos} (OSD on top), Overlay zpos={OverlayZpos} (video below)",
            primaryZpos, overlayZpos);

        var primarySuccess = _presenter.PrimaryPlane.SetPlaneZPosition(primaryZpos);
        var overlaySuccess = _presenter.OverlayPlane.SetPlaneZPosition(overlayZpos);

        if (primarySuccess && overlaySuccess)
        {
            _logger.LogInformation("Z-positioning successful: OSD will render on top of video");
        }
        else
        {
            _logger.LogWarning("Failed to set z-positions - OSD may not appear on top of video");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _logger.LogInformation("Disposing DRM rendering context");

        _imguiManager?.Dispose();
        _inputManager?.Dispose();
        _presenter?.Dispose();
        _gbmDevice?.Dispose();

        // Only dispose buffer manager if we own it
        if (_ownsBufferManager)
        {
            _drmBufferManager.Dispose();
        }
    }
}
