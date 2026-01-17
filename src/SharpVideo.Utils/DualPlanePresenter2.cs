using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using Microsoft.Extensions.Logging;

using SharpVideo.Drm;
using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.C;

namespace SharpVideo.Utils;

/// <summary>
/// Cached atomic properties for CRTC and connector atomic modesetting.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class AtomicModesetProperties
{
    /// <summary>CRTC "ACTIVE" property ID</summary>
    public uint CrtcActivePropertyId { get; init; }
    
    /// <summary>CRTC "MODE_ID" property ID</summary>
    public uint CrtcModeIdPropertyId { get; init; }
    
    /// <summary>Connector "CRTC_ID" property ID</summary>
    public uint ConnectorCrtcIdPropertyId { get; init; }

    public bool IsValid => CrtcActivePropertyId != 0 && 
                          CrtcModeIdPropertyId != 0 && 
                          ConnectorCrtcIdPropertyId != 0;

    public static unsafe AtomicModesetProperties Query(int drmFd, uint crtcId, uint connectorId)
    {
        return new AtomicModesetProperties
        {
            CrtcActivePropertyId = GetObjectPropertyId(drmFd, crtcId, LibDrm.DRM_MODE_OBJECT_CRTC, "ACTIVE"),
            CrtcModeIdPropertyId = GetObjectPropertyId(drmFd, crtcId, LibDrm.DRM_MODE_OBJECT_CRTC, "MODE_ID"),
            ConnectorCrtcIdPropertyId = GetObjectPropertyId(drmFd, connectorId, LibDrm.DRM_MODE_OBJECT_CONNECTOR, "CRTC_ID"),
        };
    }

    private static unsafe uint GetObjectPropertyId(int drmFd, uint objectId, uint objectType, string propertyName)
    {
        var props = LibDrm.drmModeObjectGetProperties(drmFd, objectId, objectType);
        if (props == null)
            return 0;

        try
        {
            for (int i = 0; i < props->CountProps; i++)
            {
                var propId = props->Props[i];
                var prop = LibDrm.drmModeGetProperty(drmFd, propId);
                if (prop == null)
                    continue;

                try
                {
                    var name = prop->NameString;
                    if (name != null && name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        return propId;
                    }
                }
                finally
                {
                    LibDrm.drmModeFreeProperty(prop);
                }
            }
        }
        finally
        {
            LibDrm.drmModeFreeObjectProperties(props);
        }

        return 0;
    }
}

/// <summary>
/// High-performance dual-plane DRM presenter optimized for minimal video latency.
/// Uses atomic modesetting with blocking commits and a dedicated commit thread.
/// 
/// Features:
/// - Atomic-only: Uses DRM atomic modesetting for tearless updates
/// - Blocking commits: Each commit blocks until vsync for precise timing
/// - Latest frame wins: New video frames replace pending ones immediately
/// - Automatic commit triggering: New frame arrival triggers immediate commit attempt
/// - Dedicated commit thread with elevated priority
/// 
/// Buffer lifecycle:
/// - Maximum 3 buffers in flight per plane (pending, committed, displaying)
/// - Released buffers returned immediately via EnqueueVideoFrame return value
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class DualPlanePresenter2 : IDisposable
{
    private readonly DrmDevice _drmDevice;
    private readonly DualPlanePresenterConfig _config;
    private readonly ILogger? _logger;

    // Atomic properties
    private readonly AtomicModesetProperties _modesetProps;
    private readonly AtomicPlaneProperties? _videoPlaneProps;
    private readonly AtomicPlaneProperties? _osdPlaneProps;
    
    // Mode blob
    private uint _modeBlobId;
    
    // OSD framebuffer cache
    private readonly OsdFramebufferCache? _osdFbCache;
    
    // Video plane buffer tracking (max 3 in flight)
    private volatile SharedDmaBuffer? _pendingVideoBuffer;
    private SharedDmaBuffer? _committedVideoBuffer;
    private SharedDmaBuffer? _displayingVideoBuffer;
    private SharedDmaBuffer? _releasedVideoBuffer;
    
    // OSD plane buffer tracking (max 3 in flight)
    private volatile nint _pendingOsdBo;
    private nint _committedOsdBo;
    private nint _displayingOsdBo;
    private nint _releasedOsdBo;
    private uint _currentOsdFbId;
    
    // Commit thread synchronization
    private readonly AutoResetEvent _commitSignal = new(false);
    private readonly CancellationTokenSource _cts = new();
    private Thread? _commitThread;
    private bool _started;
    
    // Page flip event handling
    private readonly LibDrm.DrmEventPageFlipHandler _pageFlipHandler;
    private readonly GCHandle _gcHandle;
    private DrmEventContext _eventContext;
    
    private bool _disposed;

    /// <summary>
    /// Creates a new dual-plane presenter with the specified configuration.
    /// </summary>
    /// <param name="drmDevice">The DRM device.</param>
    /// <param name="config">The presenter configuration.</param>
    /// <exception cref="ArgumentNullException">If drmDevice or config is null.</exception>
    /// <exception cref="InvalidOperationException">If configuration is invalid or atomic modesetting is not supported.</exception>
    public DualPlanePresenter2(DrmDevice drmDevice, DualPlanePresenterConfig config)
    {
        ArgumentNullException.ThrowIfNull(drmDevice);
        ArgumentNullException.ThrowIfNull(config);
        
        config.Validate();
        
        _drmDevice = drmDevice;
        _config = config;
        _logger = config.Logger;

        // Enable atomic modesetting
        if (!drmDevice.TrySetClientCapability(DrmClientCapability.DRM_CLIENT_CAP_ATOMIC, true, out var atomicResult))
        {
            throw new InvalidOperationException(
                $"Atomic modesetting not supported by this DRM device. Error code: {atomicResult}");
        }
        
        _logger?.LogDebug("Atomic modesetting enabled");

        // Query atomic properties for modeset
        _modesetProps = AtomicModesetProperties.Query(drmDevice.DeviceFd, config.CrtcId, config.ConnectorId);
        if (!_modesetProps.IsValid)
        {
            throw new InvalidOperationException(
                "Failed to query required atomic properties for CRTC/connector modeset");
        }

        // Cache plane properties
        if (config.VideoPlaneEnabled && config.VideoPlane != null)
        {
            _videoPlaneProps = new AtomicPlaneProperties(config.VideoPlane);
            if (!_videoPlaneProps.IsValid())
            {
                throw new InvalidOperationException($"Video plane {config.VideoPlane.Id} lacks required atomic properties");
            }
        }

        if (config.OsdPlaneEnabled && config.OsdPlane != null)
        {
            _osdPlaneProps = new AtomicPlaneProperties(config.OsdPlane);
            if (!_osdPlaneProps.IsValid())
            {
                throw new InvalidOperationException($"OSD plane {config.OsdPlane.Id} lacks required atomic properties");
            }
            
            // Create OSD framebuffer cache
            var osdDrawConfig = config.OsdDrawConfig!.Value;
            // Use AR24 format for OSD (ARGB8888)
            _osdFbCache = new OsdFramebufferCache(
                drmDevice.DeviceFd,
                osdDrawConfig.SrcWidth,
                osdDrawConfig.SrcHeight,
                0x34325241); // DRM_FORMAT_ARGB8888
        }

        // Setup page flip event handler
        _pageFlipHandler = OnPageFlipComplete;
        _gcHandle = GCHandle.Alloc(_pageFlipHandler);
        _eventContext = new DrmEventContext
        {
            version = LibDrm.DRM_EVENT_CONTEXT_VERSION,
            page_flip_handler = Marshal.GetFunctionPointerForDelegate(_pageFlipHandler)
        };

        // Perform modeset
        PerformModeset();

        _logger?.LogInformation(
            "DualPlanePresenter2 initialized: CRTC={CrtcId}, VideoPlane={VideoPlane}, OsdPlane={OsdPlane}",
            config.CrtcId,
            config.VideoPlaneEnabled ? config.VideoPlane?.Id.ToString() : "disabled",
            config.OsdPlaneEnabled ? config.OsdPlane?.Id.ToString() : "disabled");
    }

    /// <summary>
    /// Enqueues a video frame for display. Returns immediately.
    /// If a frame was already pending and not yet committed, it is returned immediately.
    /// </summary>
    /// <param name="buffer">The video buffer to display. Must have a valid FramebufferId.</param>
    /// <returns>
    /// The previous pending buffer that was replaced (caller should return it to decoder),
    /// or null if no buffer was replaced.
    /// </returns>
    /// <exception cref="InvalidOperationException">If video plane is not enabled.</exception>
    public SharedDmaBuffer? EnqueueVideoFrame(SharedDmaBuffer buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (!_config.VideoPlaneEnabled)
        {
            throw new InvalidOperationException("Video plane is not enabled");
        }
        
        ArgumentNullException.ThrowIfNull(buffer);
        
        if (buffer.FramebufferId == 0)
        {
            throw new ArgumentException("Buffer must have a valid FramebufferId", nameof(buffer));
        }

        // Atomically replace pending buffer
        var previous = Interlocked.Exchange(ref _pendingVideoBuffer, buffer);
        
        // Signal commit thread
        _commitSignal.Set();
        
        return previous;
    }

    /// <summary>
    /// Gets released video buffers that have finished displaying and can be returned to the decoder.
    /// </summary>
    /// <param name="destination">Span to copy released buffers into.</param>
    /// <returns>Number of buffers copied (0 or 1).</returns>
    public int GetReleasedVideoBuffers(Span<SharedDmaBuffer> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (destination.IsEmpty)
            return 0;

        var released = Interlocked.Exchange(ref _releasedVideoBuffer, null);
        if (released != null)
        {
            destination[0] = released;
            return 1;
        }
        
        return 0;
    }

    /// <summary>
    /// Sets the OSD buffer to display.
    /// </summary>
    /// <param name="gbmBo">The GBM buffer object handle from eglSwapBuffers/gbm_surface_lock_front_buffer.</param>
    /// <exception cref="InvalidOperationException">If OSD plane is not enabled.</exception>
    public void SetOsdBuffer(nint gbmBo)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (!_config.OsdPlaneEnabled)
        {
            throw new InvalidOperationException("OSD plane is not enabled");
        }

        // Atomically replace pending OSD buffer
        Interlocked.Exchange(ref _pendingOsdBo, gbmBo);
        
        // Signal commit thread
        _commitSignal.Set();
    }

    /// <summary>
    /// Gets the released OSD buffer that has finished displaying.
    /// The caller should release this back to the GBM surface.
    /// </summary>
    /// <returns>The released buffer object handle, or 0 if none.</returns>
    public nint GetReleasedOsdBuffer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        return Interlocked.Exchange(ref _releasedOsdBo, 0);
    }

    /// <summary>
    /// Starts the commit thread. Must be called before submitting frames.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (_started)
            return;

        _started = true;
        
        _commitThread = new Thread(CommitLoop)
        {
            Name = "DualPlanePresenter2 Commit",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _commitThread.Start();
        
        _logger?.LogInformation("Commit thread started");
    }

    /// <summary>
    /// Stops the commit thread.
    /// </summary>
    public void Stop()
    {
        if (!_started || _commitThread == null)
            return;

        _cts.Cancel();
        _commitSignal.Set(); // Wake the thread
        _commitThread.Join(TimeSpan.FromSeconds(2));
        
        _started = false;
        _logger?.LogInformation("Commit thread stopped");
    }

    private unsafe void PerformModeset()
    {
        // Create mode blob
        var mode = _config.Mode;
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

        // Copy mode name
        var nameBytes = Encoding.UTF8.GetBytes(mode.NameString ?? "");
        for (int i = 0; i < Math.Min(nameBytes.Length, 32); i++)
        {
            nativeMode.Name[i] = nameBytes[i];
        }

        var blobResult = LibDrm.drmModeCreatePropertyBlob(
            _drmDevice.DeviceFd,
            &nativeMode,
            (nuint)sizeof(DrmModeModeInfo),
            out _modeBlobId);

        if (blobResult != 0)
        {
            throw new DrmException($"Failed to create mode blob: {blobResult}", _drmDevice.DeviceFd);
        }

        _logger?.LogDebug("Created mode blob ID: {BlobId}", _modeBlobId);

        // Build atomic modeset request
        var req = LibDrm.drmModeAtomicAlloc();
        if (req == null)
        {
            throw new DrmException("Failed to allocate atomic request", _drmDevice.DeviceFd);
        }

        try
        {
            // CRTC properties: MODE_ID and ACTIVE
            LibDrm.drmModeAtomicAddProperty(req, _config.CrtcId, _modesetProps.CrtcModeIdPropertyId, _modeBlobId);
            LibDrm.drmModeAtomicAddProperty(req, _config.CrtcId, _modesetProps.CrtcActivePropertyId, 1);

            // Connector CRTC_ID
            LibDrm.drmModeAtomicAddProperty(req, _config.ConnectorId, _modesetProps.ConnectorCrtcIdPropertyId, _config.CrtcId);

            // Setup video plane (attached but empty)
            if (_config.VideoPlaneEnabled && _videoPlaneProps != null)
            {
                var videoConfig = _config.VideoDrawConfig!.Value;
                var planeId = _config.VideoPlane!.Id;

                // Set geometry
                LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.CrtcIdPropertyId, _config.CrtcId);
                LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.CrtcXPropertyId, videoConfig.DstX);
                LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.CrtcYPropertyId, videoConfig.DstY);
                LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.CrtcWPropertyId, videoConfig.EffectiveDstWidth);
                LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.CrtcHPropertyId, videoConfig.EffectiveDstHeight);
                LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.SrcXPropertyId, 0);
                LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.SrcYPropertyId, 0);
                LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.SrcWPropertyId, (ulong)videoConfig.SrcWidth << 16);
                LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.SrcHPropertyId, (ulong)videoConfig.SrcHeight << 16);
                
                // FB_ID = 0 (empty initially)
                LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.FbIdPropertyId, 0);

                // Set zpos if available
                if (_videoPlaneProps.HasZpos() && _config.ZPos.HasValue)
                {
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.ZposPropertyId, _config.ZPos.Value.VideoZPos);
                }
            }

            // Setup OSD plane (attached but empty)
            if (_config.OsdPlaneEnabled && _osdPlaneProps != null)
            {
                var osdConfig = _config.OsdDrawConfig!.Value;
                var planeId = _config.OsdPlane!.Id;

                // Set geometry
                LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.CrtcIdPropertyId, _config.CrtcId);
                LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.CrtcXPropertyId, osdConfig.DstX);
                LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.CrtcYPropertyId, osdConfig.DstY);
                LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.CrtcWPropertyId, osdConfig.EffectiveDstWidth);
                LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.CrtcHPropertyId, osdConfig.EffectiveDstHeight);
                LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.SrcXPropertyId, 0);
                LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.SrcYPropertyId, 0);
                LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.SrcWPropertyId, (ulong)osdConfig.SrcWidth << 16);
                LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.SrcHPropertyId, (ulong)osdConfig.SrcHeight << 16);
                
                // FB_ID = 0 (empty initially)
                LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.FbIdPropertyId, 0);

                // Set zpos if available
                if (_osdPlaneProps.HasZpos() && _config.ZPos.HasValue)
                {
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.ZposPropertyId, _config.ZPos.Value.OsdZPos);
                }
            }

            // Commit with ALLOW_MODESET flag (blocking)
            var flags = DrmModeAtomicFlags.DRM_MODE_ATOMIC_ALLOW_MODESET;
            var commitResult = LibDrm.drmModeAtomicCommit(_drmDevice.DeviceFd, req, flags, 0);

            if (commitResult != 0)
            {
                throw new DrmException($"Modeset atomic commit failed: {commitResult}", _drmDevice.DeviceFd);
            }

            _logger?.LogInformation(
                "Modeset complete: {Width}x{Height}@{Hz}Hz",
                _config.Mode.HDisplay, _config.Mode.VDisplay, _config.Mode.VRefresh);
        }
        finally
        {
            LibDrm.drmModeAtomicFree(req);
        }
    }

    private void CommitLoop()
    {
        _logger?.LogDebug("Commit loop started");

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                // Check for pending buffers
                var hasPendingVideo = _pendingVideoBuffer != null;
                var hasPendingOsd = _pendingOsdBo != 0;

                if (!hasPendingVideo && !hasPendingOsd)
                {
                    // Wait for signal or timeout
                    _commitSignal.WaitOne(100);
                    continue;
                }

                // Grab pending buffers atomically
                var videoBuffer = Interlocked.Exchange(ref _pendingVideoBuffer, null);
                var osdBo = Interlocked.Exchange(ref _pendingOsdBo, 0);

                // Move displaying -> released, committed -> displaying
                if (_config.VideoPlaneEnabled)
                {
                    var oldDisplaying = _displayingVideoBuffer;
                    _displayingVideoBuffer = _committedVideoBuffer;
                    _committedVideoBuffer = null;
                    
                    if (oldDisplaying != null)
                    {
                        // Release previous displaying buffer
                        Interlocked.Exchange(ref _releasedVideoBuffer, oldDisplaying);
                    }
                }

                if (_config.OsdPlaneEnabled)
                {
                    var oldDisplaying = _displayingOsdBo;
                    _displayingOsdBo = _committedOsdBo;
                    _committedOsdBo = 0;
                    
                    if (oldDisplaying != 0)
                    {
                        Interlocked.Exchange(ref _releasedOsdBo, oldDisplaying);
                    }
                }

                // Build and execute atomic commit
                var success = PerformCommit(videoBuffer, osdBo);

                if (success)
                {
                    // Move grabbed buffers to committed
                    if (videoBuffer != null)
                    {
                        _committedVideoBuffer = videoBuffer;
                    }
                    
                    if (osdBo != 0)
                    {
                        _committedOsdBo = osdBo;
                    }

                    _logger?.LogTrace("Commit successful");
                }
                else
                {
                    _logger?.LogWarning("Commit failed");
                    
                    // Return buffers on failure
                    if (videoBuffer != null)
                    {
                        Interlocked.Exchange(ref _releasedVideoBuffer, videoBuffer);
                    }
                    
                    if (osdBo != 0)
                    {
                        Interlocked.Exchange(ref _releasedOsdBo, osdBo);
                    }
                }

                // Loop immediately without wait to check for new pending
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in commit loop");
            }
        }

        _logger?.LogDebug("Commit loop exited");
    }

    private unsafe bool PerformCommit(SharedDmaBuffer? videoBuffer, nint osdBo)
    {
        var req = LibDrm.drmModeAtomicAlloc();
        if (req == null)
        {
            _logger?.LogError("Failed to allocate atomic request for commit");
            return false;
        }

        try
        {
            // Video plane
            if (_config.VideoPlaneEnabled && _videoPlaneProps != null)
            {
                var planeId = _config.VideoPlane!.Id;
                uint fbId = videoBuffer?.FramebufferId ?? 0;
                
                // Only set FB_ID, geometry was set at modeset
                LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.FbIdPropertyId, fbId);
            }

            // OSD plane
            if (_config.OsdPlaneEnabled && _osdPlaneProps != null && _osdFbCache != null)
            {
                var planeId = _config.OsdPlane!.Id;
                uint fbId = 0;
                
                if (osdBo != 0)
                {
                    fbId = _osdFbCache.GetOrCreate(osdBo);
                    _currentOsdFbId = fbId;
                }
                else if (_currentOsdFbId != 0)
                {
                    // Keep displaying current OSD if no new one
                    fbId = _currentOsdFbId;
                }
                
                LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.FbIdPropertyId, fbId);
            }

            // Commit with page flip event - this blocks until vsync
            var flags = DrmModeAtomicFlags.DRM_MODE_PAGE_FLIP_EVENT;
            var result = LibDrm.drmModeAtomicCommit(_drmDevice.DeviceFd, req, flags, 0);

            if (result != 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                _logger?.LogTrace("Atomic commit failed: result={Result}, errno={Errno}", result, errno);
                return false;
            }

            // Wait for page flip event (blocking)
            WaitForPageFlip();

            return true;
        }
        finally
        {
            LibDrm.drmModeAtomicFree(req);
        }
    }

    private unsafe void WaitForPageFlip()
    {
        // Poll for DRM event
        var pollFd = new PollFd
        {
            fd = _drmDevice.DeviceFd,
            events = PollEvents.POLLIN
        };

        var timeout = (int)(1000.0 / (_config.Mode.VRefresh > 0 ? _config.Mode.VRefresh : 60) * 2);
        var ret = Libc.poll(ref pollFd, 1, timeout);

        if (ret > 0 && (pollFd.revents & PollEvents.POLLIN) != 0)
        {
            fixed (DrmEventContext* ctx = &_eventContext)
            {
                LibDrm.drmHandleEvent(_drmDevice.DeviceFd, ctx);
            }
        }
    }

    private void OnPageFlipComplete(int fd, uint sequence, uint tvSec, uint tvUsec, nint userData)
    {
        // Page flip completed - buffer transition already handled in commit loop
        _logger?.LogTrace("Page flip complete: seq={Sequence}", sequence);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Stop commit thread
        Stop();

        // Destroy mode blob
        if (_modeBlobId != 0)
        {
            LibDrm.drmModeDestroyPropertyBlob(_drmDevice.DeviceFd, _modeBlobId);
            _modeBlobId = 0;
        }

        // Dispose OSD framebuffer cache
        _osdFbCache?.Dispose();

        // Free callback handle
        if (_gcHandle.IsAllocated)
        {
            _gcHandle.Free();
        }

        _commitSignal.Dispose();
        _cts.Dispose();

        _logger?.LogDebug("DualPlanePresenter2 disposed");
    }
}
