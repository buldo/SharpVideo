using System.Runtime.Versioning;

using FFmpeg.AutoGen;

using Hexa.NET.ImGui;

using SharpVideo.Decoding.OhdDemo.Configuration;
using SharpVideo.Decoding.V4l2;
using SharpVideo.Decoding.V4l2.Stateless;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.ImGui;
using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.C;
using SharpVideo.Utils;

namespace SharpVideo.Decoding.OhdDemo.ImguiOsd;

/// <summary>
/// DRM/KMS host for ImGui-based OSD rendering with hardware video plane.
/// Uses dual-plane architecture:
/// - Primary plane (GBM/OpenGL ES): ImGui OSD with transparency
/// - Overlay plane (DMA buffers): Video frames from decoder
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class DrmHost : UiHostBase<V4l2H264StatelessDecoder, V4l2EncodedBuffer, V4l2DecodedFrame>
{
    private readonly DrmHostConfiguration _configuration;

    // DRM resources
    private DrmDevice? _drmDevice;
    private GbmDevice? _gbmDevice;
    private DmaBuffersAllocator? _dmaAllocator;
    private DrmBufferManager? _drmBufferManager;
    private DrmPresenter? _presenter;
    private InputManager? _inputManager;
    private ImGuiManager? _imguiManager;
    private VideoPlaneRenderer? _videoPlaneRenderer;
    private readonly Dictionary<SharedDmaBuffer, V4l2DecodedFrame> _framesInUseByDrm = new();

    protected override bool ShowDemoWindow => _configuration.ShowDemoWindow;

    public DrmHost(
        [FromKeyedServices("h264-stream")] InMemoryPipeStreamAccessor h264Stream,
        V4l2H264StatelessDecoder decoder,
        ILoggerFactory loggerFactory,
        ILogger<DrmHost> logger,
        DrmHostConfiguration? configuration = null)
        : base(h264Stream, decoder, loggerFactory, logger)
    {
        _configuration = configuration ?? new DrmHostConfiguration();
        Logger.LogInformation("DrmHost initialized (dual-plane mode)");
    }

    protected override void RunDrawThread()
    {
        try
        {
            Logger.LogInformation("DrawThread started");

            // Set environment for DRM
            Environment.SetEnvironmentVariable("EGL_PLATFORM", "drm");

            InitializeDrmResources();

            if (_presenter == null || _imguiManager == null)
            {
                Logger.LogError("Failed to initialize DRM resources");
                return;
            }

            // Warmup frame
            Logger.LogInformation("Rendering warmup frame...");
            var gbmAtomicPresenter = _presenter.AsGbmAtomicPresenter();
            if (gbmAtomicPresenter != null && _imguiManager.WarmupFrame(RenderOsdFrame))
            {
                if (gbmAtomicPresenter.SubmitFrame())
                {
                    Logger.LogInformation("Warmup frame submitted");
                }
            }

            Thread.Sleep(100);
            Logger.LogInformation("Display initialization completed");

            // Enter main loop
            Logger.LogInformation("Entering render loop");
            RunRenderLoop();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception in DrawThread");
        }
        finally
        {
            CleanupResources();
            Logger.LogInformation("DrawThread finished");
        }
    }

    /// <summary>
    /// Initializes all DRM resources including overlay plane.
    /// Uses decoder's OutputPixelFormat directly for the video plane.
    /// </summary>
    private void InitializeDrmResources()
    {
        // Get decoder's output format - DRM plane must support this format
        var videoPixelFormat = H264Decoder.OutputPixelFormat;

        Logger.LogInformation(
            "Initializing DRM resources. Video pixel format: {Format}",
            videoPixelFormat.GetName());

        // Open DRM device
        _drmDevice = string.IsNullOrEmpty(_configuration.DrmDevicePath)
            ? DrmUtils.OpenDrmDevice(Logger)
            : DrmDevice.Open(_configuration.DrmDevicePath);

        if (_drmDevice == null)
        {
            throw new InvalidOperationException("Failed to open DRM device");
        }

        _drmDevice.EnableDrmCapabilities(Logger);

        // Create GBM device for ImGui rendering
        _gbmDevice = GbmDevice.CreateFromDrmDevice(_drmDevice);
        Logger.LogInformation("Created GBM device for ImGui rendering");

        // Create DMA buffer allocator for video plane
        if (!DmaBuffersAllocator.TryCreate(out _dmaAllocator) || _dmaAllocator == null)
        {
            throw new InvalidOperationException("Failed to create DMA buffers allocator");
        }

        // Initialize DrmBufferManager with formats needed for this decoder
        _drmBufferManager = new DrmBufferManager(
            _drmDevice,
            _dmaAllocator,
            [videoPixelFormat, KnownPixelFormats.DRM_FORMAT_ARGB8888],
            LoggerFactory.CreateLogger<DrmBufferManager>());

        // Create unified DRM presenter with GBM atomic primary (ImGui) and DMA overlay (video)
        _presenter = DrmPresenter.CreateWithGbmAtomicAndDmaOverlay(
            _drmDevice,
            _configuration.DisplayWidth,
            _configuration.DisplayHeight,
            _gbmDevice,
            _drmBufferManager,
            KnownPixelFormats.DRM_FORMAT_ARGB8888,  // Primary plane for ImGui OSD
            videoPixelFormat,                      // Overlay plane for video (matches decoder)
            Logger);

        if (_presenter == null)
        {
            throw new InvalidOperationException("Failed to create DRM presenter");
        }

        // Get actual display dimensions (may differ from requested if fallback mode was used)
        var actualWidth = _presenter.PrimaryPlanePresenter.Width;
        var actualHeight = _presenter.PrimaryPlanePresenter.Height;

        if (actualWidth != _configuration.DisplayWidth || actualHeight != _configuration.DisplayHeight)
        {
            Logger.LogWarning(
                "Display mode differs from requested: requested {ReqWidth}x{ReqHeight}, actual {ActWidth}x{ActHeight}",
                _configuration.DisplayWidth, _configuration.DisplayHeight, actualWidth, actualHeight);
        }

        Logger.LogInformation("Created dual-plane DRM presenter ({Width}x{Height})", actualWidth, actualHeight);

        // Configure z-order: Primary plane (ImGui OSD) on top, Overlay plane (video) below
        ConfigurePlaneZOrder();

        // Initialize input manager
        if (_configuration.EnableInput)
        {
            _inputManager = new InputManager(
                actualWidth,
                actualHeight,
                LoggerFactory.CreateLogger<InputManager>());
            Logger.LogInformation("Input manager initialized");
        }

        // Get GBM atomic presenter for ImGui
        var gbmAtomicPresenter = _presenter.AsGbmAtomicPresenter();
        if (gbmAtomicPresenter == null)
        {
            throw new InvalidOperationException("Failed to get GBM atomic presenter");
        }

        // Configure ImGui with actual display dimensions
        var imguiConfig = new ImGuiDrmConfiguration
        {
            Width = actualWidth,
            Height = actualHeight,
            DrmDevice = _drmDevice,
            GbmDevice = _gbmDevice,
            GbmSurfaceHandle = gbmAtomicPresenter.GetNativeGbmSurfaceHandle(),
            PixelFormat = KnownPixelFormats.DRM_FORMAT_ARGB8888,
            ConfigFlags = ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.DockingEnable,
            DrawCursor = true,
            UiScale = _configuration.UiScale,
            GlslVersion = "#version 300 es",
            EnableInput = _configuration.EnableInput
        };

        _imguiManager = new ImGuiManager(
            imguiConfig,
            _inputManager,
            LoggerFactory.CreateLogger<ImGuiManager>());

        Logger.LogInformation("ImGui manager initialized");

        // Create video plane renderer for overlay
        _videoPlaneRenderer = new VideoPlaneRenderer(
            _presenter.OverlayPlanePresenter,
            _drmBufferManager,
            videoPixelFormat,
            LoggerFactory.CreateLogger<VideoPlaneRenderer>());

        Logger.LogInformation("DRM resources initialized successfully (dual-plane mode)");
    }

    private void ConfigurePlaneZOrder()
    {
        if (_presenter == null)
        {
            return;
        }

        Logger.LogInformation("Configuring plane z-order...");

        var primaryZposRange = _presenter.PrimaryPlane.GetPlaneZPositionRange();
        var overlayZposRange = _presenter.OverlayPlane.GetPlaneZPositionRange();

        if (primaryZposRange.HasValue)
        {
            Logger.LogInformation("Primary plane zpos range: [{Min}, {Max}], current: {Current}",
                primaryZposRange.Value.min, primaryZposRange.Value.max, primaryZposRange.Value.current);
        }
        else
        {
            Logger.LogWarning("Primary plane does not support zpos property");
        }

        if (overlayZposRange.HasValue)
        {
            Logger.LogInformation("Overlay plane zpos range: [{Min}, {Max}], current: {Current}",
                overlayZposRange.Value.min, overlayZposRange.Value.max, overlayZposRange.Value.current);
        }
        else
        {
            Logger.LogWarning("Overlay plane does not support zpos property");
        }

        // Set z-position to make primary plane (OSD) appear on top of overlay (video)
        if (primaryZposRange.HasValue && overlayZposRange.HasValue)
        {
            var primaryZpos = primaryZposRange.Value.max;
            var overlayZpos = overlayZposRange.Value.min;

            Logger.LogInformation("Setting Primary zpos={PrimaryZpos} (OSD on top), Overlay zpos={OverlayZpos} (video below)",
                primaryZpos, overlayZpos);

            var primarySuccess = _presenter.PrimaryPlane.SetPlaneZPosition(primaryZpos);
            var overlaySuccess = _presenter.OverlayPlane.SetPlaneZPosition(overlayZpos);

            if (primarySuccess && overlaySuccess)
            {
                Logger.LogInformation("Z-positioning successful: OSD will render on top of video");
            }
            else
            {
                Logger.LogWarning("Failed to set z-positions - OSD may not appear on top of video");
            }
        }
    }

    private void RunRenderLoop()
    {
        var exiting = false;
        var frameCount = 0;
        var inputFd = _inputManager?.GetFileDescriptor() ?? -1;
        var gbmAtomicPresenter = _presenter?.AsGbmAtomicPresenter();

        if (gbmAtomicPresenter == null)
        {
            Logger.LogError("Failed to get GBM atomic presenter for render loop");
            return;
        }

        while (!exiting && !CancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                // Poll input events (non-blocking)
                if (_inputManager != null && inputFd >= 0)
                {
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

                    // Check for ESC key to exit
                    if (_inputManager.IsKeyDown(1)) // KEY_ESC = 1
                    {
                        Logger.LogInformation("ESC key pressed, exiting");
                        exiting = true;
                        continue;
                    }
                }

                // Render video frame to overlay plane
                RenderVideoFrame();

                // Render OSD frame to primary plane
                var osdFrameRendered = _imguiManager!.RenderFrame(RenderOsdFrame);

                if (osdFrameRendered)
                {
                    if (gbmAtomicPresenter.SubmitFrame())
                    {
                        frameCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Exception in render loop on frame {Frame}", frameCount);
                exiting = true;
            }
        }

        Logger.LogInformation("Render loop exited after {FrameCount} frames", frameCount);
    }

    private void RenderVideoFrame()
    {
        // Release buffers that DRM has finished displaying
        ReleaseCompletedFrames();

        var frame = VideoFrameManager?.AcquireCurrentFrame();
        if (frame != null)
        {
            Logger.LogTrace("Rendering video frame to overlay plane");

            // Update UI statistics based on frame type
            UpdateFrameStatistics(frame);

            // If it's a V4L2 DMA-BUF frame, we must hold it until DRM finishes displaying it
            if (frame is V4l2DecodedFrame { IsDmaBuf: true, DmaBuffer: not null } v4l2Frame)
            {
                lock (_framesInUseByDrm)
                {
                    _framesInUseByDrm[v4l2Frame.DmaBuffer] = frame;
                }
                _videoPlaneRenderer?.RenderFrame(frame);
            }
            else
            {
                // For other frame types (FFmpeg or MMAP), a copy occurs,
                // so the original frame can be released immediately
                _videoPlaneRenderer?.RenderFrame(frame);
                VideoFrameManager?.ReleaseFrame(frame);
            }

            Logger.LogTrace("Video frame presented");
        }
    }

    private void ReleaseCompletedFrames()
    {
        if (_presenter?.OverlayPlanePresenter == null) return;

        var completedBuffers = _presenter.OverlayPlanePresenter.GetPresentedOverlayBuffers();
        if (completedBuffers.Length > 0)
        {
            lock (_framesInUseByDrm)
            {
                foreach (var buffer in completedBuffers)
                {
                    if (_framesInUseByDrm.Remove(buffer, out var frameToRelease))
                    {
                        VideoFrameManager?.ReleaseFrame(frameToRelease);
                    }
                }
            }
        }
    }

    private unsafe void UpdateFrameStatistics(V4l2DecodedFrame frame)
    {
        UiRenderer?.UpdateFrameStatistics(
            (int)frame.Width,
            (int)frame.Height,
            0, // V4L2 doesn't use AVPixelFormat
            0, // No PTS available from V4L2
            false); // Key frame detection not available
    }

    private void RenderOsdFrame(float deltaTime)
    {
        // Render ImGui UI (OSD)
        UiRenderer?.RenderUi(deltaTime);
    }

    private void CleanupResources()
    {
        Logger.LogInformation("Cleaning up DRM resources");

        // Release any frames still in use by DRM
        lock (_framesInUseByDrm)
        {
            foreach (var frame in _framesInUseByDrm.Values)
            {
                VideoFrameManager?.ReleaseFrame(frame);
            }
            _framesInUseByDrm.Clear();
        }

        _videoPlaneRenderer?.Dispose();
        _imguiManager?.Dispose();
        _inputManager?.Dispose();
        _presenter?.Dispose();
        _drmBufferManager?.Dispose();
        _gbmDevice?.Dispose();
        _drmDevice?.Dispose();
    }
}
