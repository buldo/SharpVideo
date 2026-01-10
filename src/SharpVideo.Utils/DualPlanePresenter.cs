using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using Microsoft.Extensions.Logging;

using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.C;
using SharpVideo.Linux.Native.Gbm;

namespace SharpVideo.Utils;

/// <summary>
/// Configuration for a plane in the dual-plane presenter.
/// </summary>
public record PlaneConfig(
    DrmPlane Plane,
    AtomicPlaneProperties Properties,
    uint Width,
    uint Height,
    ulong Zpos);

/// <summary>
/// Unified dual-plane atomic presenter for OSD + Video rendering.
/// Manages two DRM planes with configurable z-ordering:
/// - OSD plane (primary): GBM/OpenGL ES with transparency, renders on top
/// - Video plane (overlay): DMA buffers for zero-copy video, renders below
/// 
/// Uses a single event loop thread for both planes, avoiding dual event loop conflicts.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class DualPlanePresenter : IDisposable
{
    private readonly DrmDevice _drmDevice;
    private readonly uint _crtcId;
    private readonly uint _connectorId;
    private readonly DrmModeInfo _mode;
    private readonly ILogger _logger;

    // OSD plane (GBM surface for OpenGL ES)
    private readonly PlaneConfig _osdPlaneConfig;
    private readonly GbmSurface _gbmSurface;
    private readonly PixelFormat _osdPixelFormat;
    private readonly Dictionary<nint, BufferInfo> _osdBufferCache = new();

    // Video plane (DMA buffers)
    private readonly PlaneConfig _videoPlaneConfig;
    private readonly DrmBufferManager _bufferManager;

    // Thread synchronization
    private readonly Thread _eventThread;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _stateLock = new();
    private readonly GCHandle _gcHandle;

    // OSD state
    private readonly ConcurrentQueue<QueuedBuffer> _osdRenderQueue = new();
    private QueuedBuffer? _osdCurrentDisplayed;
    private QueuedBuffer? _osdPendingFlip;
    private bool _osdFlipInProgress;

    // Video state
    private SharedDmaBuffer? _videoLatestBuffer;
    private uint _videoLatestFbId;
    private SharedDmaBuffer? _videoCurrentDisplayed;
    private SharedDmaBuffer? _videoPendingBuffer; // Buffer that was committed and waiting for flip
    private readonly Queue<SharedDmaBuffer> _videoCompletedBuffers = new();
    private bool _videoFlipInProgress;

    // Event handling
    private readonly LibDrm.DrmEventPageFlipHandler _pageFlipHandler;
    private DrmEventContext _eventContext;

    // Constants for user_data to identify which plane triggered the event
    private const nint OsdPlaneUserData = 1;
    private const nint VideoPlaneUserData = 2;

    private bool _initialized;
    private bool _disposed;

    private struct QueuedBuffer
    {
        public nint Bo;
        public uint FbId;
    }

    private struct BufferInfo
    {
        public uint FbId;
        public uint Handle;
    }

    /// <summary>
    /// Gets the display width.
    /// </summary>
    public uint Width => _mode.HDisplay;

    /// <summary>
    /// Gets the display height.
    /// </summary>
    public uint Height => _mode.VDisplay;

    /// <summary>
    /// Gets the native GBM surface handle for EGL context creation.
    /// </summary>
    public nint GbmSurfaceHandle => _gbmSurface.Handle;

    private DualPlanePresenter(
        DrmDevice drmDevice,
        uint crtcId,
        uint connectorId,
        DrmModeInfo mode,
        PlaneConfig osdPlaneConfig,
        GbmSurface gbmSurface,
        PixelFormat osdPixelFormat,
        PlaneConfig videoPlaneConfig,
        DrmBufferManager bufferManager,
        ILogger logger)
    {
        _drmDevice = drmDevice;
        _crtcId = crtcId;
        _connectorId = connectorId;
        _mode = mode;
        _osdPlaneConfig = osdPlaneConfig;
        _gbmSurface = gbmSurface;
        _osdPixelFormat = osdPixelFormat;
        _videoPlaneConfig = videoPlaneConfig;
        _bufferManager = bufferManager;
        _logger = logger;

        // Setup page flip event handling
        _pageFlipHandler = OnPageFlipComplete;
        _gcHandle = GCHandle.Alloc(_pageFlipHandler);

        _eventContext = new DrmEventContext
        {
            version = LibDrm.DRM_EVENT_CONTEXT_VERSION,
            page_flip_handler = Marshal.GetFunctionPointerForDelegate(_pageFlipHandler)
        };

        // Start event loop thread
        _eventThread = new Thread(EventLoopThread)
        {
            Name = "DualPlane Event Loop",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _eventThread.Start();

        _logger.LogInformation(
            "DualPlanePresenter initialized: OSD zpos={OsdZpos}, Video zpos={VideoZpos}",
            osdPlaneConfig.Zpos, videoPlaneConfig.Zpos);
    }

    /// <summary>
    /// Creates a dual-plane presenter with OSD on top and video below.
    /// </summary>
    public static DualPlanePresenter Create(
        DrmDevice drmDevice,
        GbmDevice gbmDevice,
        DrmBufferManager bufferManager,
        uint requestedWidth,
        uint requestedHeight,
        PixelFormat osdPixelFormat,
        PixelFormat videoPixelFormat,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(drmDevice);
        ArgumentNullException.ThrowIfNull(gbmDevice);
        ArgumentNullException.ThrowIfNull(bufferManager);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var logger = loggerFactory.CreateLogger<DualPlanePresenter>();

        // Enable atomic mode
        if (!drmDevice.TrySetClientCapability(DrmClientCapability.DRM_CLIENT_CAP_ATOMIC, true, out var result))
        {
            throw new NotSupportedException(
                $"Atomic modesetting is not supported by this DRM device. Error code: {result}");
        }

        // Get DRM resources
        var resources = drmDevice.GetResources()
            ?? throw new InvalidOperationException("Failed to get DRM resources");

        // Find connector, encoder, CRTC, mode
        var (connector, mode, crtcId) = SetupDisplay(drmDevice, resources, requestedWidth, requestedHeight, logger);

        // Find planes
        var crtcIndex = resources.Crtcs.ToList().IndexOf(crtcId);
        var compatiblePlanes = resources.Planes
            .Where(p => (p.PossibleCrtcs & (1u << crtcIndex)) != 0)
            .ToList();

        var osdPlane = FindPlaneByType(compatiblePlanes, "Primary", osdPixelFormat)
            ?? throw new InvalidOperationException("No primary plane found for OSD");

        var videoPlane = FindPlaneByType(compatiblePlanes, "Overlay", videoPixelFormat)
            ?? throw new InvalidOperationException($"No overlay plane found for video format {videoPixelFormat.GetName()}");

        logger.LogInformation(
            "Found planes: OSD (primary) ID={OsdId}, Video (overlay) ID={VideoId}",
            osdPlane.Id, videoPlane.Id);

        // Get properties
        var osdProps = new AtomicPlaneProperties(osdPlane);
        var videoProps = new AtomicPlaneProperties(videoPlane);

        if (!osdProps.IsValid() || !videoProps.IsValid())
        {
            throw new InvalidOperationException("Atomic properties not available for planes");
        }

        // Determine z-order: OSD on top, video below
        var osdZposRange = osdPlane.GetPlaneZPositionRange();
        var videoZposRange = videoPlane.GetPlaneZPositionRange();

        ulong osdZpos = 1;
        ulong videoZpos = 0;

        if (osdZposRange.HasValue && videoZposRange.HasValue)
        {
            osdZpos = (ulong)osdZposRange.Value.max;
            videoZpos = (ulong)videoZposRange.Value.min;
            logger.LogInformation(
                "Z-order configured: OSD zpos={OsdZpos} (top), Video zpos={VideoZpos} (bottom)",
                osdZpos, videoZpos);
        }
        else
        {
            logger.LogWarning("Zpos not supported, using default layer ordering");
        }

        // Create GBM surface for OSD
        var gbmSurface = gbmDevice.CreateSurface(
            mode.HDisplay,
            mode.VDisplay,
            osdPixelFormat,
            GbmBoUse.GBM_BO_USE_SCANOUT | GbmBoUse.GBM_BO_USE_RENDERING);

        logger.LogInformation("Created GBM surface {Width}x{Height} for OSD", mode.HDisplay, mode.VDisplay);

        var osdConfig = new PlaneConfig(osdPlane, osdProps, mode.HDisplay, mode.VDisplay, osdZpos);
        var videoConfig = new PlaneConfig(videoPlane, videoProps, mode.HDisplay, mode.VDisplay, videoZpos);

        return new DualPlanePresenter(
            drmDevice,
            crtcId,
            connector.ConnectorId,
            mode,
            osdConfig,
            gbmSurface,
            osdPixelFormat,
            videoConfig,
            bufferManager,
            logger);
    }

    /// <summary>
    /// Submits an OSD frame (from eglSwapBuffers) for display.
    /// Non-blocking, returns immediately. Frame displayed at next vblank.
    /// </summary>
    /// <returns>True if frame was queued, false if dropped (queue full)</returns>
    public bool SubmitOsdFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        nint newBo = 0;
        bool shouldRelease = false;

        try
        {
            newBo = LibGbm.LockFrontBuffer(_gbmSurface.Handle);
            if (newBo == 0)
            {
                _logger.LogError("Failed to lock front buffer from GBM surface");
                return false;
            }

            var fbId = GetOrCreateOsdFramebuffer(newBo);
            if (fbId == 0)
            {
                _logger.LogError("Failed to create framebuffer for OSD");
                shouldRelease = true;
                return false;
            }

            var queuedBuffer = new QueuedBuffer { Bo = newBo, FbId = fbId };

            lock (_stateLock)
            {
                // Drop frame if queue is full or flip in progress
                if (!_osdRenderQueue.IsEmpty || _osdFlipInProgress)
                {
                    shouldRelease = true;
                    return false;
                }

                _osdRenderQueue.Enqueue(queuedBuffer);
                newBo = 0; // Ownership transferred
            }

            return true;
        }
        finally
        {
            if (shouldRelease && newBo != 0)
            {
                LibGbm.ReleaseBuffer(_gbmSurface.Handle, newBo);
            }
        }
    }

    /// <summary>
    /// Submits a video frame (DMA buffer) for display.
    /// Always shows the latest frame, dropping older ones.
    /// </summary>
    public void SubmitVideoFrame(SharedDmaBuffer dmaBuffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(dmaBuffer);

        // Ensure framebuffer exists
        if (dmaBuffer.FramebufferId == 0)
        {
            dmaBuffer.FramebufferId = _bufferManager.CreateFramebuffer(dmaBuffer);
        }

        if (dmaBuffer.FramebufferId == 0)
        {
            _logger.LogWarning("Failed to create framebuffer for video frame");
            return;
        }

        lock (_stateLock)
        {
            // If there's a previous pending frame that wasn't committed, mark it as completed
            if (_videoLatestBuffer != null && _videoLatestBuffer != dmaBuffer)
            {
                _videoCompletedBuffers.Enqueue(_videoLatestBuffer);
            }

            _videoLatestBuffer = dmaBuffer;
            _videoLatestFbId = dmaBuffer.FramebufferId;
        }
    }

    /// <summary>
    /// Gets video buffers that have been displayed and can be returned to the decoder.
    /// </summary>
    public SharedDmaBuffer[] GetCompletedVideoBuffers()
    {
        lock (_stateLock)
        {
            if (_videoCompletedBuffers.Count == 0)
            {
                return [];
            }

            var result = _videoCompletedBuffers.ToArray();
            _videoCompletedBuffers.Clear();
            return result;
        }
    }

    private void EventLoopThread()
    {
        _logger.LogInformation("Dual-plane event loop started");

        var pollFd = new PollFd
        {
            fd = _drmDevice.DeviceFd,
            events = PollEvents.POLLIN
        };

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                // Try to start flips for pending frames
                TryCommitPendingFrames();

                // Wait for page flip events
                var timeout = Math.Max(5, (int)(1000.0 / _mode.VRefresh * 0.9));
                var ret = Libc.poll(ref pollFd, 1, timeout);

                if (ret > 0 && (pollFd.revents & PollEvents.POLLIN) != 0)
                {
                    unsafe
                    {
                        fixed (DrmEventContext* evctxPtr = &_eventContext)
                        {
                            LibDrm.drmHandleEvent(_drmDevice.DeviceFd, evctxPtr);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in dual-plane event loop");
            }
        }

        _logger.LogInformation("Dual-plane event loop stopped");
    }

    private void TryCommitPendingFrames()
    {
        lock (_stateLock)
        {
            // Initialize display with first OSD frame
            if (!_initialized && _osdRenderQueue.TryDequeue(out var firstOsd))
            {
                InitializeDisplay(firstOsd);
                return;
            }

            if (!_initialized)
            {
                return;
            }

            // Commit OSD if not in progress
            if (!_osdFlipInProgress && _osdRenderQueue.TryDequeue(out var osdBuffer))
            {
                CommitOsdFrame(osdBuffer);
            }

            // Commit video if not in progress and we have a new frame
            if (!_videoFlipInProgress && _videoLatestFbId != 0)
            {
                CommitVideoFrame();
            }
        }
    }

    private void InitializeDisplay(QueuedBuffer osdBuffer)
    {
        _logger.LogInformation("Initializing display with first OSD frame");

        // Set CRTC mode
        unsafe
        {
            var nativeMode = new DrmModeModeInfo
            {
                Clock = _mode.Clock,
                HDisplay = _mode.HDisplay,
                HSyncStart = _mode.HSyncStart,
                HSyncEnd = _mode.HSyncEnd,
                HTotal = _mode.HTotal,
                HSkew = _mode.HSkew,
                VDisplay = _mode.VDisplay,
                VSyncStart = _mode.VSyncStart,
                VSyncEnd = _mode.VSyncEnd,
                VTotal = _mode.VTotal,
                VScan = _mode.VScan,
                VRefresh = _mode.VRefresh,
                Flags = _mode.Flags,
                Type = _mode.Type
            };

            var nameBytes = Encoding.UTF8.GetBytes(_mode.Name);
            for (int i = 0; i < Math.Min(nameBytes.Length, 32); i++)
            {
                nativeMode.Name[i] = nameBytes[i];
            }

            var connectorId = _connectorId;

            var ret = LibDrm.drmModeSetCrtc(
                _drmDevice.DeviceFd,
                _crtcId,
                osdBuffer.FbId,
                0, 0,
                &connectorId, 1,
                &nativeMode);

            if (ret != 0)
            {
                _logger.LogWarning("drmModeSetCrtc failed: {Error}", ret);
            }
        }

        _osdCurrentDisplayed = osdBuffer;
        _initialized = true;

        _logger.LogInformation("Display initialized");
    }

    private unsafe void CommitOsdFrame(QueuedBuffer buffer)
    {
        var req = LibDrm.drmModeAtomicAlloc();
        if (req == null)
        {
            ReleaseOsdBuffer(buffer);
            return;
        }

        try
        {
            var props = _osdPlaneConfig.Properties;
            var planeId = _osdPlaneConfig.Plane.Id;

            AddPlaneProperties(req, planeId, props, buffer.FbId, _crtcId, _osdPlaneConfig.Width, _osdPlaneConfig.Height);

            // Set zpos
            if (props.HasZpos())
            {
                LibDrm.drmModeAtomicAddProperty(req, planeId, props.ZposPropertyId, _osdPlaneConfig.Zpos);
            }

            var flags = DrmModeAtomicFlags.DRM_MODE_ATOMIC_NONBLOCK | DrmModeAtomicFlags.DRM_MODE_PAGE_FLIP_EVENT;

            var ret = LibDrm.drmModeAtomicCommit(_drmDevice.DeviceFd, req, flags, OsdPlaneUserData);
            if (ret == 0)
            {
                _osdFlipInProgress = true;
                _osdPendingFlip = buffer;
            }
            else
            {
                _logger.LogTrace("OSD atomic commit failed: {Error}", ret);
                ReleaseOsdBuffer(buffer);
            }
        }
        finally
        {
            LibDrm.drmModeAtomicFree(req);
        }
    }

    private unsafe void CommitVideoFrame()
    {
        var req = LibDrm.drmModeAtomicAlloc();
        if (req == null)
        {
            return;
        }

        try
        {
            var props = _videoPlaneConfig.Properties;
            var planeId = _videoPlaneConfig.Plane.Id;

            AddPlaneProperties(req, planeId, props, _videoLatestFbId, _crtcId, _videoPlaneConfig.Width, _videoPlaneConfig.Height);

            // Set zpos
            if (props.HasZpos())
            {
                LibDrm.drmModeAtomicAddProperty(req, planeId, props.ZposPropertyId, _videoPlaneConfig.Zpos);
            }

            var flags = DrmModeAtomicFlags.DRM_MODE_ATOMIC_NONBLOCK | DrmModeAtomicFlags.DRM_MODE_PAGE_FLIP_EVENT;

            var ret = LibDrm.drmModeAtomicCommit(_drmDevice.DeviceFd, req, flags, VideoPlaneUserData);
            if (ret == 0)
            {
                _videoFlipInProgress = true;
                // Save the buffer we committed for later tracking
                _videoPendingBuffer = _videoLatestBuffer;
                // Clear pending, we've committed it
                _videoLatestFbId = 0;
            }
            else
            {
                _logger.LogTrace("Video atomic commit failed: {Error}", ret);
            }
        }
        finally
        {
            LibDrm.drmModeAtomicFree(req);
        }
    }

    private static unsafe void AddPlaneProperties(
        DrmModeAtomicReq* req,
        uint planeId,
        AtomicPlaneProperties props,
        uint fbId,
        uint crtcId,
        uint width,
        uint height)
    {
        LibDrm.drmModeAtomicAddProperty(req, planeId, props.FbIdPropertyId, fbId);
        LibDrm.drmModeAtomicAddProperty(req, planeId, props.CrtcIdPropertyId, crtcId);
        LibDrm.drmModeAtomicAddProperty(req, planeId, props.CrtcXPropertyId, 0);
        LibDrm.drmModeAtomicAddProperty(req, planeId, props.CrtcYPropertyId, 0);
        LibDrm.drmModeAtomicAddProperty(req, planeId, props.CrtcWPropertyId, width);
        LibDrm.drmModeAtomicAddProperty(req, planeId, props.CrtcHPropertyId, height);
        LibDrm.drmModeAtomicAddProperty(req, planeId, props.SrcXPropertyId, 0);
        LibDrm.drmModeAtomicAddProperty(req, planeId, props.SrcYPropertyId, 0);
        LibDrm.drmModeAtomicAddProperty(req, planeId, props.SrcWPropertyId, width << 16);
        LibDrm.drmModeAtomicAddProperty(req, planeId, props.SrcHPropertyId, height << 16);
    }

    private void OnPageFlipComplete(int fd, uint sequence, uint tv_sec, uint tv_usec, nint user_data)
    {
        lock (_stateLock)
        {
            // Use user_data to identify which plane triggered this event
            if (user_data == OsdPlaneUserData)
            {
                // Handle OSD flip completion
                _osdFlipInProgress = false;

                if (_osdCurrentDisplayed.HasValue)
                {
                    ReleaseOsdBuffer(_osdCurrentDisplayed.Value);
                }

                _osdCurrentDisplayed = _osdPendingFlip;
                _osdPendingFlip = null;
            }
            else if (user_data == VideoPlaneUserData)
            {
                // Handle video flip completion
                _videoFlipInProgress = false;

                if (_videoCurrentDisplayed != null)
                {
                    _videoCompletedBuffers.Enqueue(_videoCurrentDisplayed);
                }

                // Use the pending buffer that was saved during commit
                _videoCurrentDisplayed = _videoPendingBuffer;
                _videoPendingBuffer = null;
            }
            else
            {
                // Unknown user_data - might be from initialization or other source
                // Handle both flags for safety (legacy behavior)
                if (_osdFlipInProgress)
                {
                    _osdFlipInProgress = false;
                    if (_osdCurrentDisplayed.HasValue)
                    {
                        ReleaseOsdBuffer(_osdCurrentDisplayed.Value);
                    }
                    _osdCurrentDisplayed = _osdPendingFlip;
                    _osdPendingFlip = null;
                }

                if (_videoFlipInProgress)
                {
                    _videoFlipInProgress = false;
                    if (_videoCurrentDisplayed != null)
                    {
                        _videoCompletedBuffers.Enqueue(_videoCurrentDisplayed);
                    }
                    _videoCurrentDisplayed = _videoPendingBuffer;
                    _videoPendingBuffer = null;
                }
            }
        }
    }

    private uint GetOrCreateOsdFramebuffer(nint bo)
    {
        if (_osdBufferCache.TryGetValue(bo, out var bufferInfo))
        {
            return bufferInfo.FbId;
        }

        var width = LibGbm.GetWidth(bo);
        var height = LibGbm.GetHeight(bo);
        var stride = LibGbm.GetStride(bo);
        var handle = LibGbm.GetHandle(bo);

        if (handle == 0)
        {
            return 0;
        }

        unsafe
        {
            uint* handles = stackalloc uint[4];
            uint* pitches = stackalloc uint[4];
            uint* offsets = stackalloc uint[4];

            handles[0] = handle;
            pitches[0] = stride;
            offsets[0] = 0;

            for (int i = 1; i < 4; i++)
            {
                handles[i] = 0;
                pitches[i] = 0;
                offsets[i] = 0;
            }

            var result = LibDrm.drmModeAddFB2(
                _drmDevice.DeviceFd,
                width, height,
                _osdPixelFormat.Fourcc,
                handles, pitches, offsets,
                out var fbId, 0);

            if (result != 0)
            {
                return 0;
            }

            _osdBufferCache[bo] = new BufferInfo { FbId = fbId, Handle = handle };
            return fbId;
        }
    }

    private void ReleaseOsdBuffer(QueuedBuffer buffer)
    {
        LibGbm.ReleaseBuffer(_gbmSurface.Handle, buffer.Bo);
    }

    private static (DrmConnector connector, DrmModeInfo mode, uint crtcId) SetupDisplay(
        DrmDevice drmDevice,
        DrmDeviceResources resources,
        uint width,
        uint height,
        ILogger logger)
    {
        var connector = resources.Connectors.FirstOrDefault(c => c.Connection == DrmModeConnection.Connected)
            ?? throw new InvalidOperationException("No connected display found");

        logger.LogInformation("Found connected display: {Type}", connector.ConnectorType);

        // Find best mode
        var mode = connector.Modes
            .Where(m => m.HDisplay == width && m.VDisplay == height)
            .OrderByDescending(m => m.VRefresh)
            .FirstOrDefault();

        if (mode == null)
        {
            mode = connector.Modes
                .OrderByDescending(m => m.VRefresh)
                .ThenByDescending(m => (long)m.HDisplay * m.VDisplay)
                .FirstOrDefault();
        }

        if (mode == null)
        {
            throw new InvalidOperationException("No display modes available");
        }

        logger.LogInformation("Using mode: {Width}x{Height}@{Hz}Hz", mode.HDisplay, mode.VDisplay, mode.VRefresh);

        var encoder = connector.Encoder ?? connector.Encoders.FirstOrDefault()
            ?? throw new InvalidOperationException("No encoder found");

        var crtcId = encoder.CrtcId;
        if (crtcId == 0)
        {
            var crtcsArray = resources.Crtcs.ToArray();
            crtcId = resources.Crtcs
                .Where(crtc => (encoder.PossibleCrtcs & (1u << Array.IndexOf(crtcsArray, crtc))) != 0)
                .FirstOrDefault();
        }

        if (crtcId == 0)
        {
            throw new InvalidOperationException("No available CRTC found");
        }

        return (connector, mode, crtcId);
    }

    private static DrmPlane? FindPlaneByType(List<DrmPlane> planes, string typeName, PixelFormat format)
    {
        return planes.FirstOrDefault(p =>
        {
            var props = p.GetProperties();
            var typeProp = props.FirstOrDefault(prop => prop.Name.Equals("type", StringComparison.OrdinalIgnoreCase));
            bool isCorrectType = typeProp != null && typeProp.EnumNames != null &&
                                 typeProp.Value < (ulong)typeProp.EnumNames.Count &&
                                 typeProp.EnumNames[(int)typeProp.Value].Equals(typeName, StringComparison.OrdinalIgnoreCase);
            return isCorrectType && p.Formats.Contains(format.Fourcc);
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logger.LogInformation("Disposing DualPlanePresenter");

        // Stop event loop
        _cts.Cancel();
        if (!_eventThread.Join(TimeSpan.FromSeconds(2)))
        {
            _logger.LogWarning("Event loop thread did not stop gracefully");
        }

        // Disable planes
        try
        {
            LibDrm.drmModeSetPlane(_drmDevice.DeviceFd, _osdPlaneConfig.Plane.Id, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            LibDrm.drmModeSetPlane(_drmDevice.DeviceFd, _videoPlaneConfig.Plane.Id, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to disable planes");
        }

        // Clean up buffers
        lock (_stateLock)
        {
            // Clean up OSD buffers
            if (_osdCurrentDisplayed.HasValue)
            {
                ReleaseOsdBuffer(_osdCurrentDisplayed.Value);
            }

            if (_osdPendingFlip.HasValue)
            {
                ReleaseOsdBuffer(_osdPendingFlip.Value);
            }

            while (_osdRenderQueue.TryDequeue(out var buffer))
            {
                ReleaseOsdBuffer(buffer);
            }

            // Clean up OSD framebuffers
            foreach (var info in _osdBufferCache.Values)
            {
                try
                {
                    LibDrm.drmModeRmFB(_drmDevice.DeviceFd, info.FbId);
                }
                catch { }
            }
            _osdBufferCache.Clear();

            // Move all video buffers to completed queue so they can be returned to decoder
            // Note: We don't own these buffers, just mark them as completed
            if (_videoCurrentDisplayed != null)
            {
                _videoCompletedBuffers.Enqueue(_videoCurrentDisplayed);
                _videoCurrentDisplayed = null;
            }

            if (_videoPendingBuffer != null)
            {
                _videoCompletedBuffers.Enqueue(_videoPendingBuffer);
                _videoPendingBuffer = null;
            }

            if (_videoLatestBuffer != null)
            {
                _videoCompletedBuffers.Enqueue(_videoLatestBuffer);
                _videoLatestBuffer = null;
                _videoLatestFbId = 0;
            }
        }

        // Dispose GBM surface
        try
        {
            _gbmSurface.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose GBM surface");
        }

        // Clean up GC handle
        if (_gcHandle.IsAllocated)
        {
            _gcHandle.Free();
        }

        _cts.Dispose();

        _logger.LogInformation("DualPlanePresenter disposed");
    }
}
