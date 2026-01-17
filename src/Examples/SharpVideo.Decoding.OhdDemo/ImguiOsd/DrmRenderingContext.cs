using System.Runtime.Versioning;
using System.Text;

using Hexa.NET.ImGui;

using SharpVideo.Decoding.OhdDemo.Configuration;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.ImGui;
using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.C;
using SharpVideo.Utils;
using SharpVideo.Utils.Buffers;

namespace SharpVideo.Decoding.OhdDemo.ImguiOsd;

/// <summary>
/// Encapsulates DRM/KMS resource initialization and management.
/// Handles device setup, dual-plane configuration, and ImGui integration.
/// </summary>
/// <remarks>
/// Uses DualPlanePresenter2 for unified management of OSD and video planes.
/// OSD plane renders on top of video plane via zpos configuration.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class DrmRenderingContext : IDisposable
{
    private readonly ILogger _logger;
    private readonly DrmBufferManager _drmBufferManager;
    private readonly bool _ownsBufferManager;

    private GbmDevice? _gbmDevice;
    private GbmSurface? _gbmSurface;
    private DualPlanePresenter2? _presenter;
    private InputManager? _inputManager;
    private ImGuiManager? _imguiManager;

    private bool _disposed;
    private bool _exitRequested;

    /// <summary>
    /// Gets the DRM buffer manager for video plane allocation.
    /// </summary>
    public DrmBufferManager BufferManager => _drmBufferManager;

    /// <summary>
    /// Gets the dual-plane presenter for OSD and video rendering.
    /// </summary>
    public DualPlanePresenter2 Presenter =>
        _presenter ?? throw new InvalidOperationException("Context not initialized");

    /// <summary>
    /// Gets the GBM surface for OSD rendering.
    /// </summary>
    public GbmSurface GbmSurface =>
        _gbmSurface ?? throw new InvalidOperationException("Context not initialized");

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

        if (_presenter == null || _imguiManager == null || _gbmSurface == null)
        {
            _logger.LogWarning("Presenter or ImGui manager not available for warmup");
            return false;
        }

        if (!_imguiManager.WarmupFrame(renderCallback))
        {
            return false;
        }

        // Lock front buffer and submit to presenter
        var osdBo = GbmSurfaceHelper.LockFrontBuffer(_gbmSurface, _logger);
        if (osdBo == 0)
        {
            return false;
        }

        var releasedBuffers = new nint[4];
        var (replacedBo, _) = _presenter.SetOsdBuffer(osdBo, releasedBuffers);
        if (replacedBo != 0)
        {
            GbmSurfaceHelper.ReleaseBuffer(_gbmSurface, replacedBo);
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
    /// Renders an OSD frame using ImGui and submits it to the display.
    /// </summary>
    /// <returns>True if frame was rendered and submitted successfully.</returns>
    public bool RenderOsdFrame(ImGuiRenderDelegate renderCallback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_presenter == null || _imguiManager == null || _gbmSurface == null)
        {
            return false;
        }

        if (!_imguiManager.RenderFrame(renderCallback))
        {
            return false;
        }

        // Lock front buffer and submit to presenter
        var osdBo = GbmSurfaceHelper.LockFrontBuffer(_gbmSurface, _logger);
        if (osdBo == 0)
        {
            return false;
        }

        var releasedBuffers = new nint[4];
        var (replacedBo, releasedCount) = _presenter.SetOsdBuffer(osdBo, releasedBuffers);

        // Release replaced buffer
        if (replacedBo != 0)
        {
            GbmSurfaceHelper.ReleaseBuffer(_gbmSurface, replacedBo);
        }

        // Release any additional buffers that finished displaying
        for (int i = 0; i < releasedCount; i++)
        {
            GbmSurfaceHelper.ReleaseBuffer(_gbmSurface, releasedBuffers[i]);
        }

        return true;
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

        // Get device resources
        var resources = drmDevice.GetResources();
        if (resources == null)
        {
            throw new InvalidOperationException("Failed to get DRM resources");
        }

        // Find connected connector
        var connector = resources.Connectors
            .FirstOrDefault(c => c.Connection == DrmModeConnection.Connected);

        if (connector == null)
        {
            throw new InvalidOperationException("No connected display found");
        }

        _logger.LogInformation("Found connector: {Type}-{TypeId} (ID: {Id})",
            connector.ConnectorType, connector.ConnectorTypeId, connector.ConnectorId);

        // Get display mode
        var mode = connector.Modes
            .FirstOrDefault(m => m.HDisplay == configuration.DisplayWidth && m.VDisplay == configuration.DisplayHeight)
            ?? connector.Modes.FirstOrDefault();

        if (mode == null)
        {
            throw new InvalidOperationException("No suitable display mode found");
        }

        // Set actual display dimensions
        DisplayWidth = (uint)mode.HDisplay;
        DisplayHeight = (uint)mode.VDisplay;

        if (DisplayWidth != configuration.DisplayWidth || DisplayHeight != configuration.DisplayHeight)
        {
            _logger.LogWarning(
                "Display mode differs from requested: requested {ReqWidth}x{ReqHeight}, actual {ActWidth}x{ActHeight}",
                configuration.DisplayWidth, configuration.DisplayHeight, DisplayWidth, DisplayHeight);
        }

        // Get CRTC ID
        uint crtcId = connector.Encoder?.CrtcId ?? 0;
        if (crtcId == 0)
        {
            crtcId = resources.Crtcs.FirstOrDefault();
            if (crtcId == 0)
            {
                throw new InvalidOperationException("No CRTC found");
            }
        }

        _logger.LogInformation("Using CRTC: {CrtcId}", crtcId);

        // Create GBM device for ImGui rendering
        _gbmDevice = GbmDevice.CreateFromDrmDevice(drmDevice);
        _logger.LogInformation("Created GBM device for ImGui rendering");

        // Select planes for video and OSD
        DualPlaneSelection planeSelection;
        try
        {
            planeSelection = DualPlaneSelector.Select(
                drmDevice,
                crtcId,
                videoPixelFormat.Fourcc,                      // Video plane format
                KnownPixelFormats.DRM_FORMAT_ARGB8888.Fourcc, // OSD plane format
                _logger);

            _logger.LogInformation("Selected planes - Video: {VideoId}, OSD: {OsdId}",
                planeSelection.VideoPlane.Id, planeSelection.OsdPlane.Id);
        }
        catch (DrmException ex)
        {
            throw new InvalidOperationException($"Failed to select planes: {ex.Message}", ex);
        }

        // Create GBM surface for OSD plane
        _gbmSurface = GbmSurfaceHelper.CreateForRendering(
            _gbmDevice,
            DisplayWidth,
            DisplayHeight,
            KnownPixelFormats.DRM_FORMAT_ARGB8888,
            _logger);

        // Build presenter configuration
        var presenterConfig = DualPlanePresenterConfig.CreateBuilder()
            .WithVideoPlane(planeSelection.VideoPlane, new PlaneDrawConfiguration(DisplayWidth, DisplayHeight))
            .WithOsdPlane(planeSelection.OsdPlane, new PlaneDrawConfiguration(DisplayWidth, DisplayHeight))
            .WithCrtc(crtcId)
            .WithConnector(connector.ConnectorId)
            .WithMode(ConvertToNativeMode(mode))
            .WithZPos(planeSelection.ZPos)
            .WithLogger(_logger)
            .Build();

        // Create the dual-plane presenter
        _presenter = new DualPlanePresenter2(drmDevice, presenterConfig);
        _presenter.Start();

        _logger.LogInformation("Created dual-plane presenter ({Width}x{Height})", DisplayWidth, DisplayHeight);

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
        var imguiConfig = new ImGuiDrmConfiguration
        {
            Width = DisplayWidth,
            Height = DisplayHeight,
            DrmDevice = drmDevice,
            GbmDevice = _gbmDevice!,
            GbmSurfaceHandle = _gbmSurface!.Handle,
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

    /// <summary>
    /// Converts a managed DrmModeInfo to native DrmModeModeInfo.
    /// </summary>
    private static DrmModeModeInfo ConvertToNativeMode(DrmModeInfo mode)
    {
        var nativeMode = new DrmModeModeInfo
        {
            Clock = mode.Clock,
            HDisplay = mode.HDisplay,
            HSyncStart = mode.HSyncStart,
            HSyncEnd = mode.HSyncEnd,
            HTotal = mode.HTotal,
            HSkew = mode.HSkew,
            VDisplay = mode.VDisplay,
            VSyncStart = mode.VSyncStart,
            VSyncEnd = mode.VSyncEnd,
            VTotal = mode.VTotal,
            VScan = mode.VScan,
            VRefresh = mode.VRefresh,
            Flags = mode.Flags,
            Type = mode.Type
        };

        unsafe
        {
            var nameBytes = Encoding.UTF8.GetBytes(mode.Name);
            for (int i = 0; i < Math.Min(nameBytes.Length, 32); i++)
            {
                nativeMode.Name[i] = nameBytes[i];
            }
        }

        return nativeMode;
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

        // Drain any remaining OSD buffers before stopping presenter
        if (_presenter != null && _gbmSurface != null)
        {
            var releasedBuffers = new nint[4];
            var releasedCount = _presenter.GetReleasedOsdBuffers(releasedBuffers);
            for (int i = 0; i < releasedCount; i++)
            {
                GbmSurfaceHelper.ReleaseBuffer(_gbmSurface, releasedBuffers[i]);
            }
        }

        _presenter?.Stop();
        _presenter?.Dispose();

        _gbmSurface?.Dispose();
        _gbmDevice?.Dispose();

        // Only dispose buffer manager if we own it
        if (_ownsBufferManager)
        {
            _drmBufferManager.Dispose();
        }
    }
}
