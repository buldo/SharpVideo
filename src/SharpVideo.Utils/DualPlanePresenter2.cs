using System.Collections.Concurrent;
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

    // Framebuffer caches
    private readonly VideoFramebufferCache? _videoFbCache;
    private readonly OsdFramebufferCache? _osdFbCache;

    // Video plane buffer tracking (max 3 in flight)
    private volatile SharedDmaBuffer? _pendingVideoBuffer;
    private SharedDmaBuffer? _committedVideoBuffer;
    private SharedDmaBuffer? _displayingVideoBuffer;
    private readonly ConcurrentQueue<SharedDmaBuffer> _releasedVideoBuffers = new();

    // OSD plane buffer tracking (max 3 in flight)
    private volatile nint _pendingOsdBo;
    private nint _committedOsdBo;
    private nint _displayingOsdBo;
    private readonly ConcurrentQueue<nint> _releasedOsdBuffers = new();
    private uint _currentOsdFbId;

    // Commit thread synchronization
    private readonly AutoResetEvent _commitSignal = new(false);
    private readonly CancellationTokenSource _cts = new();
    private Thread? _commitThread;
    private bool _started;

    // Plane initialization tracking (planes are configured on first commit with valid FB)
    private bool _videoPlaneInitialized;
    private bool _osdPlaneInitialized;

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

            // Create video framebuffer cache
            _videoFbCache = new VideoFramebufferCache(drmDevice.DeviceFd, _logger);
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
                0x34325241, // DRM_FORMAT_ARGB8888
                _logger);
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
    /// If a frame was already pending and not yet committed, it is returned as 'replaced'.
    /// Also drains any released buffers into the destination span.
    /// The framebuffer is created automatically from the DMA buffer if needed.
    /// </summary>
    /// <param name="buffer">The video buffer to display.</param>
    /// <param name="releasedDestination">Span to copy released buffers into.</param>
    /// <returns>
    /// A tuple containing:
    /// - replaced: The previous pending buffer that was replaced (caller should return it to decoder), or null if none.
    /// - releasedCount: Number of released buffers copied into releasedDestination.
    /// </returns>
    /// <exception cref="InvalidOperationException">If video plane is not enabled.</exception>
    public (SharedDmaBuffer? replaced, int releasedCount) EnqueueVideoFrame(
        SharedDmaBuffer buffer,
        Span<SharedDmaBuffer> releasedDestination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_config.VideoPlaneEnabled)
        {
            throw new InvalidOperationException("Video plane is not enabled");
        }

        ArgumentNullException.ThrowIfNull(buffer);

        // Atomically replace pending buffer
        var replaced = Interlocked.Exchange(ref _pendingVideoBuffer, buffer);

        // Signal commit thread
        _commitSignal.Set();

        // Drain released buffers
        int releasedCount = 0;
        while (releasedCount < releasedDestination.Length &&
               _releasedVideoBuffers.TryDequeue(out var released))
        {
            releasedDestination[releasedCount++] = released;
        }

        return (replaced, releasedCount);
    }

    /// <summary>
    /// Gets released video buffers that have finished displaying and can be returned to the decoder.
    /// Drains all available buffers from the release queue.
    /// </summary>
    /// <param name="destination">Span to copy released buffers into.</param>
    /// <returns>Number of buffers copied.</returns>
    public int GetReleasedVideoBuffers(Span<SharedDmaBuffer> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (destination.IsEmpty)
            return 0;

        int count = 0;
        while (count < destination.Length && _releasedVideoBuffers.TryDequeue(out var released))
        {
            destination[count++] = released;
        }

        return count;
    }

    /// <summary>
    /// Sets the OSD buffer to display.
    /// If a buffer was already pending and not yet committed, it is returned as 'replaced'.
    /// Also drains any released buffers into the destination span.
    /// </summary>
    /// <param name="gbmBo">The GBM buffer object handle from eglSwapBuffers/gbm_surface_lock_front_buffer.</param>
    /// <param name="releasedDestination">Span to copy released buffers into.</param>
    /// <returns>
    /// A tuple containing:
    /// - replaced: The previous pending buffer that was replaced, or 0 if none.
    /// - releasedCount: Number of released buffers copied into releasedDestination.
    /// </returns>
    /// <exception cref="InvalidOperationException">If OSD plane is not enabled.</exception>
    public (nint replaced, int releasedCount) SetOsdBuffer(nint gbmBo, Span<nint> releasedDestination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_config.OsdPlaneEnabled)
        {
            throw new InvalidOperationException("OSD plane is not enabled");
        }

        // Atomically replace pending OSD buffer
        var replaced = Interlocked.Exchange(ref _pendingOsdBo, gbmBo);

        // Signal commit thread
        _commitSignal.Set();

        // Drain released buffers
        int releasedCount = 0;
        while (releasedCount < releasedDestination.Length &&
               _releasedOsdBuffers.TryDequeue(out var released))
        {
            releasedDestination[releasedCount++] = released;
        }

        return (replaced, releasedCount);
    }

    /// <summary>
    /// Gets released OSD buffers that have finished displaying.
    /// The caller should release these back to the GBM surface.
    /// Drains all available buffers from the release queue.
    /// </summary>
    /// <param name="destination">Span to copy released buffers into.</param>
    /// <returns>Number of buffers copied.</returns>
    public int GetReleasedOsdBuffers(Span<nint> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (destination.IsEmpty)
            return 0;

        int count = 0;
        while (count < destination.Length && _releasedOsdBuffers.TryDequeue(out var released))
        {
            destination[count++] = released;
        }

        return count;
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
    /// Stops the commit thread and moves all in-flight buffers to the release queues.
    /// Call GetReleasedVideoBuffers/GetReleasedOsdBuffers after Stop to retrieve all buffers.
    /// </summary>
    public void Stop()
    {
        if (!_started || _commitThread == null)
            return;

        _cts.Cancel();
        _commitSignal.Set(); // Wake the thread
        _commitThread.Join(TimeSpan.FromSeconds(2));

        _started = false;

        // Drain all in-flight buffers to release queues
        var pendingVideo = Interlocked.Exchange(ref _pendingVideoBuffer, null);
        if (pendingVideo != null)
            _releasedVideoBuffers.Enqueue(pendingVideo);

        if (_committedVideoBuffer != null)
        {
            _releasedVideoBuffers.Enqueue(_committedVideoBuffer);
            _committedVideoBuffer = null;
        }

        if (_displayingVideoBuffer != null)
        {
            _releasedVideoBuffers.Enqueue(_displayingVideoBuffer);
            _displayingVideoBuffer = null;
        }

        var pendingOsd = Interlocked.Exchange(ref _pendingOsdBo, 0);
        if (pendingOsd != 0)
            _releasedOsdBuffers.Enqueue(pendingOsd);

        if (_committedOsdBo != 0)
        {
            _releasedOsdBuffers.Enqueue(_committedOsdBo);
            _committedOsdBo = 0;
        }

        if (_displayingOsdBo != 0)
        {
            _releasedOsdBuffers.Enqueue(_displayingOsdBo);
            _displayingOsdBo = 0;
        }

        _logger?.LogInformation("Commit thread stopped, in-flight buffers moved to release queues");
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

            // NOTE: We do NOT configure planes during initial modeset.
            // Some drivers (notably VC4 on Raspberry Pi) reject planes with FB_ID=0.
            // Planes will be configured on first commit when actual framebuffers are available.

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

                // Don't commit only OSD if video plane is not yet initialized
                // OSD will be committed together with video on the first video frame
                if (!_videoPlaneInitialized && !hasPendingVideo && hasPendingOsd)
                {
                    _logger?.LogTrace("Waiting for video before initializing OSD plane");
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
                        _releasedVideoBuffers.Enqueue(oldDisplaying);
                    }
                }

                if (_config.OsdPlaneEnabled)
                {
                    var oldDisplaying = _displayingOsdBo;
                    _displayingOsdBo = _committedOsdBo;
                    _committedOsdBo = 0;

                    if (oldDisplaying != 0)
                    {
                        _releasedOsdBuffers.Enqueue(oldDisplaying);
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
                        _releasedVideoBuffers.Enqueue(videoBuffer);
                    }

                    if (osdBo != 0)
                    {
                        _releasedOsdBuffers.Enqueue(osdBo);
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
            if (_config.VideoPlaneEnabled && _videoPlaneProps != null && _videoFbCache != null && videoBuffer != null)
            {
                var planeId = _config.VideoPlane!.Id;

                // Get or create framebuffer from cache
                uint fbId = _videoFbCache.GetOrCreate(videoBuffer);
                if (fbId == 0)
                {
                    _logger?.LogError("Failed to create framebuffer for video buffer");
                    return false;
                }

                // On first commit with valid FB, configure full plane geometry
                if (!_videoPlaneInitialized)
                {
                    var videoConfig = _config.VideoDrawConfig!.Value;

                    _logger?.LogDebug(
                        "Video plane {PlaneId} geometry: CRTC=({X},{Y},{W},{H}), SRC=({SrcW},{SrcH}), SrcShifted=({SrcWS},{SrcHS})",
                        planeId,
                        videoConfig.DstX, videoConfig.DstY, videoConfig.EffectiveDstWidth, videoConfig.EffectiveDstHeight,
                        videoConfig.SrcWidth, videoConfig.SrcHeight,
                        (ulong)videoConfig.SrcWidth << 16, (ulong)videoConfig.SrcHeight << 16);

                    LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.CrtcXPropertyId, videoConfig.DstX);
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.CrtcYPropertyId, videoConfig.DstY);
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.CrtcWPropertyId, videoConfig.EffectiveDstWidth);
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.CrtcHPropertyId, videoConfig.EffectiveDstHeight);
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.SrcXPropertyId, 0);
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.SrcYPropertyId, 0);
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.SrcWPropertyId, (ulong)videoConfig.SrcWidth << 16);
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.SrcHPropertyId, (ulong)videoConfig.SrcHeight << 16);

                    // Set zpos if available, requested, and NOT immutable
                    if (_videoPlaneProps.HasZpos() && _config.ZPos.HasValue && !_config.ZPos.Value.VideoZPosImmutable)
                    {
                        _logger?.LogDebug("Video plane {PlaneId} setting zpos={ZPos}", planeId, _config.ZPos.Value.VideoZPos);
                        LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.ZposPropertyId, _config.ZPos.Value.VideoZPos);
                    }
                    else if (_config.ZPos.HasValue)
                    {
                        _logger?.LogDebug("Video plane {PlaneId} zpos={ZPos} (immutable, not setting)", planeId, _config.ZPos.Value.VideoZPos);
                    }
                }

                // CRTC_ID and FB_ID must be set in every atomic commit
                LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.CrtcIdPropertyId, _config.CrtcId);
                LibDrm.drmModeAtomicAddProperty(req, planeId, _videoPlaneProps.FbIdPropertyId, fbId);

                _logger?.LogTrace("Video plane {PlaneId}: CRTC_ID={CrtcId}, FB_ID={FbId}", planeId, _config.CrtcId, fbId);
            }

            // OSD plane
            if (_config.OsdPlaneEnabled && _osdPlaneProps != null && _osdFbCache != null)
            {
                var planeId = _config.OsdPlane!.Id;
                uint fbId = 0;

                if (osdBo != 0)
                {
                    _logger?.LogTrace("OSD: Creating FB for BO={Bo}", osdBo);
                    fbId = _osdFbCache.GetOrCreate(osdBo);
                    if (fbId == 0)
                    {
                        _logger?.LogError("OSD: Failed to create framebuffer for BO={Bo}", osdBo);
                    }
                    else
                    {
                        _logger?.LogTrace("OSD: Got FB_ID={FbId} for BO={Bo}", fbId, osdBo);
                    }
                    _currentOsdFbId = fbId;
                }
                else if (_currentOsdFbId != 0)
                {
                    // Keep displaying current OSD if no new one
                    fbId = _currentOsdFbId;
                    _logger?.LogTrace("OSD: Using existing FB_ID={FbId}", fbId);
                }
                else
                {
                    _logger?.LogTrace("OSD: No buffer available (osdBo=0, currentFbId=0)");
                }

                // On first commit with valid FB, configure full plane geometry
                if (!_osdPlaneInitialized && fbId != 0)
                {
                    var osdConfig = _config.OsdDrawConfig!.Value;

                    _logger?.LogDebug(
                        "OSD plane {PlaneId} geometry: CRTC=({X},{Y},{W},{H}), SRC=({SrcW},{SrcH})",
                        planeId,
                        osdConfig.DstX, osdConfig.DstY, osdConfig.EffectiveDstWidth, osdConfig.EffectiveDstHeight,
                        osdConfig.SrcWidth, osdConfig.SrcHeight);

                    LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.CrtcXPropertyId, osdConfig.DstX);
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.CrtcYPropertyId, osdConfig.DstY);
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.CrtcWPropertyId, osdConfig.EffectiveDstWidth);
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.CrtcHPropertyId, osdConfig.EffectiveDstHeight);
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.SrcXPropertyId, 0);
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.SrcYPropertyId, 0);
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.SrcWPropertyId, (ulong)osdConfig.SrcWidth << 16);
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.SrcHPropertyId, (ulong)osdConfig.SrcHeight << 16);

                    // Set zpos if available, requested, and NOT immutable
                    if (_osdPlaneProps.HasZpos() && _config.ZPos.HasValue && !_config.ZPos.Value.OsdZPosImmutable)
                    {
                        _logger?.LogDebug("OSD plane {PlaneId} setting zpos={ZPos}", planeId, _config.ZPos.Value.OsdZPos);
                        LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.ZposPropertyId, _config.ZPos.Value.OsdZPos);
                    }
                    else if (_config.ZPos.HasValue)
                    {
                        _logger?.LogDebug("OSD plane {PlaneId} zpos={ZPos} (immutable, not setting)", planeId, _config.ZPos.Value.OsdZPos);
                    }

                    _logger?.LogDebug("OSD plane {PlaneId} initialized with geometry", planeId);
                }

                if (fbId != 0)
                {
                    // CRTC_ID and FB_ID must be set in every atomic commit
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.CrtcIdPropertyId, _config.CrtcId);
                    LibDrm.drmModeAtomicAddProperty(req, planeId, _osdPlaneProps.FbIdPropertyId, fbId);
                    _logger?.LogTrace("OSD plane {PlaneId}: CRTC_ID={CrtcId}, FB_ID={FbId}", planeId, _config.CrtcId, fbId);
                }
            }

            // Commit with page flip event - this blocks until vsync
            // Use ALLOW_MODESET on first plane initialization
            DrmModeAtomicFlags flags;
            bool needsModeset = !_videoPlaneInitialized || !_osdPlaneInitialized;

            if (needsModeset)
            {
                // First commit with plane initialization - blocking modeset
                // Use ALLOW_MODESET without NONBLOCK for synchronous completion
                flags = DrmModeAtomicFlags.DRM_MODE_ATOMIC_ALLOW_MODESET;
                _logger?.LogTrace("Commit with ALLOW_MODESET (blocking)");
            }
            else
            {
                // Normal commit - use page flip event for vsync
                flags = DrmModeAtomicFlags.DRM_MODE_PAGE_FLIP_EVENT;
            }

            var result = LibDrm.drmModeAtomicCommit(_drmDevice.DeviceFd, req, flags, 0);

            if (result != 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                _logger?.LogTrace("Atomic commit failed: result={Result}, errno={Errno}", result, errno);
                return false;
            }

            // Mark planes as initialized after successful commit
            if (videoBuffer != null && !_videoPlaneInitialized)
            {
                _videoPlaneInitialized = true;
                _logger?.LogDebug("Video plane initialized successfully");
            }
            if (_currentOsdFbId != 0 && !_osdPlaneInitialized)
            {
                _osdPlaneInitialized = true;
                _logger?.LogDebug("OSD plane initialized successfully");
            }

            // Wait for page flip event only if we used PAGE_FLIP_EVENT flag
            if (!needsModeset)
            {
                WaitForPageFlip();
            }

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

        // Dispose framebuffer caches
        _videoFbCache?.Dispose();
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
