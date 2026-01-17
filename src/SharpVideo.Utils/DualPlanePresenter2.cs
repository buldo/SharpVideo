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

    /// <summary>CRTC "OUT_FENCE_PTR" property ID for fence-based synchronization</summary>
    public uint OutFencePtrPropertyId { get; init; }

    public bool IsValid => CrtcActivePropertyId != 0 &&
                          CrtcModeIdPropertyId != 0 &&
                          ConnectorCrtcIdPropertyId != 0 &&
                          OutFencePtrPropertyId != 0;

    public static unsafe AtomicModesetProperties Query(int drmFd, uint crtcId, uint connectorId)
    {
        return new AtomicModesetProperties
        {
            CrtcActivePropertyId = GetObjectPropertyId(drmFd, crtcId, LibDrm.DRM_MODE_OBJECT_CRTC, "ACTIVE"),
            CrtcModeIdPropertyId = GetObjectPropertyId(drmFd, crtcId, LibDrm.DRM_MODE_OBJECT_CRTC, "MODE_ID"),
            ConnectorCrtcIdPropertyId = GetObjectPropertyId(drmFd, connectorId, LibDrm.DRM_MODE_OBJECT_CONNECTOR, "CRTC_ID"),
            OutFencePtrPropertyId = GetObjectPropertyId(drmFd, crtcId, LibDrm.DRM_MODE_OBJECT_CRTC, "OUT_FENCE_PTR"),
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
    private volatile SharedDmaBuffer? _committedVideoBuffer;
    private volatile SharedDmaBuffer? _displayingVideoBuffer;
    private readonly ConcurrentQueue<SharedDmaBuffer> _releasedVideoBuffers = new();

    // OSD plane buffer tracking (max 3 in flight)
    private volatile nint _pendingOsdBo;
    private volatile nint _committedOsdBo;
    private volatile nint _displayingOsdBo;
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

    // Fence-based synchronization
    private int _outFenceFd = -1;
    private readonly int _frameTimeMs;

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

        // Calculate frame time for adaptive timeouts
        var vrefresh = config.Mode.VRefresh > 0 ? config.Mode.VRefresh : 60;
        _frameTimeMs = (int)Math.Ceiling(1000.0 / vrefresh);

        // Perform modeset
        PerformModeset();

        // Mark disabled planes as already initialized to skip needsModeset checks
        _videoPlaneInitialized = !config.VideoPlaneEnabled;
        _osdPlaneInitialized = !config.OsdPlaneEnabled;

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

        if (!_commitThread.Join(TimeSpan.FromSeconds(2)))
        {
            _logger?.LogWarning("Commit thread did not terminate within timeout, some buffers may be lost");
        }

        _started = false;

        // Drain all in-flight buffers to release queues
        var pendingVideo = Interlocked.Exchange(ref _pendingVideoBuffer, null);
        if (pendingVideo != null)
            _releasedVideoBuffers.Enqueue(pendingVideo);

        var committedVideo = _committedVideoBuffer;
        if (committedVideo != null)
        {
            _releasedVideoBuffers.Enqueue(committedVideo);
            _committedVideoBuffer = null;
        }

        var displayingVideo = _displayingVideoBuffer;
        if (displayingVideo != null)
        {
            _releasedVideoBuffers.Enqueue(displayingVideo);
            _displayingVideoBuffer = null;
        }

        var pendingOsd = Interlocked.Exchange(ref _pendingOsdBo, 0);
        if (pendingOsd != 0)
            _releasedOsdBuffers.Enqueue(pendingOsd);

        var committedOsd = _committedOsdBo;
        if (committedOsd != 0)
        {
            _releasedOsdBuffers.Enqueue(committedOsd);
            _committedOsdBo = 0;
        }

        var displayingOsd = _displayingOsdBo;
        if (displayingOsd != 0)
        {
            _releasedOsdBuffers.Enqueue(displayingOsd);
            _displayingOsdBo = 0;
        }

        // Close any pending fence
        if (_outFenceFd >= 0)
        {
            Libc.close(_outFenceFd);
            _outFenceFd = -1;
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
                // MUST wait for previous fence to signal before next commit
                // DRM allows only one pending page flip per CRTC - committing
                // before previous completes causes -ENOMEM or -EBUSY
                if (_outFenceFd >= 0)
                {
                    var pollFd = new PollFd
                    {
                        fd = _outFenceFd,
                        events = PollEvents.POLLIN
                    };

                    // Always wait for fence - this is required for correct operation
                    // Use longer timeout to handle slow displays
                    var pollResult = Libc.poll(ref pollFd, 1, _frameTimeMs * 2);

                    if (pollResult > 0)
                    {
                        // Fence signaled - close it and release buffers
                        Libc.close(_outFenceFd);
                        _outFenceFd = -1;

                        // Now safe to release displaying buffers
                        if (_config.VideoPlaneEnabled)
                        {
                            var oldDisplaying = _displayingVideoBuffer;
                            _displayingVideoBuffer = _committedVideoBuffer;
                            _committedVideoBuffer = null;

                            if (oldDisplaying != null)
                            {
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
                    }
                    else if (pollResult == 0)
                    {
                        // Fence timeout - log warning but continue to prevent deadlock
                        _logger?.LogWarning("Fence wait timeout ({TimeoutMs}ms), forcing release", _frameTimeMs * 2);
                        Libc.close(_outFenceFd);
                        _outFenceFd = -1;
                        // Don't release buffers on timeout - they may still be in use
                        continue;
                    }
                    else
                    {
                        // Poll error
                        var errno = Marshal.GetLastPInvokeError();
                        _logger?.LogWarning("Fence poll failed: errno={Errno}", errno);
                        Libc.close(_outFenceFd);
                        _outFenceFd = -1;
                    }
                }

                // Check for pending buffers
                var hasPendingVideo = _pendingVideoBuffer != null;
                var hasPendingOsd = _pendingOsdBo != 0;

                if (!hasPendingVideo && !hasPendingOsd)
                {
                    // Wait for signal or timeout
                    _commitSignal.WaitOne(_frameTimeMs);
                    continue;
                }

                // Don't commit only OSD if video plane is enabled but not yet initialized
                // OSD will be committed together with video on the first video frame
                // If video plane is disabled, OSD can initialize independently
                if (_config.VideoPlaneEnabled && !_videoPlaneInitialized && !hasPendingVideo && hasPendingOsd)
                {
                    _logger?.LogTrace("Waiting for video before initializing OSD plane");
                    _commitSignal.WaitOne(_frameTimeMs);
                    continue;
                }

                // Grab pending buffers atomically
                var videoBuffer = Interlocked.Exchange(ref _pendingVideoBuffer, null);
                var osdBo = Interlocked.Exchange(ref _pendingOsdBo, 0);

                // Build and execute atomic commit
                var (success, fenceFd) = PerformCommit(videoBuffer, osdBo);

                if (success)
                {
                    // Store fence for next iteration
                    _outFenceFd = fenceFd;

                    // Move grabbed buffers to committed
                    if (videoBuffer != null)
                    {
                        _committedVideoBuffer = videoBuffer;
                    }

                    if (osdBo != 0)
                    {
                        _committedOsdBo = osdBo;
                    }

                    _logger?.LogTrace("Commit successful, fenceFd={FenceFd}", fenceFd);
                }
                else
                {
                    _logger?.LogWarning("Commit failed");

                    // Close fence if returned on failure
                    if (fenceFd >= 0)
                    {
                        Libc.close(fenceFd);
                    }

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
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in commit loop");
            }
        }

        _logger?.LogDebug("Commit loop exited");
    }

    /// <summary>
    /// Helper to add atomic property with error checking.
    /// </summary>
    private unsafe bool AddPropertyChecked(DrmModeAtomicReq* req, uint objectId, uint propId, ulong value, string propName)
    {
        var result = LibDrm.drmModeAtomicAddProperty(req, objectId, propId, value);
        if (result < 0)
        {
            _logger?.LogError("Failed to add atomic property {PropName} (id={PropId}) to object {ObjectId}: result={Result}",
                propName, propId, objectId, result);
            return false;
        }
        return true;
    }

    private unsafe (bool success, int fenceFd) PerformCommit(SharedDmaBuffer? videoBuffer, nint osdBo)
    {
        int fenceFd = -1;

        var req = LibDrm.drmModeAtomicAlloc();
        if (req == null)
        {
            _logger?.LogError("Failed to allocate atomic request for commit");
            return (false, -1);
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
                    return (false, -1);
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

                    if (!AddPropertyChecked(req, planeId, _videoPlaneProps.CrtcXPropertyId, videoConfig.DstX, "CRTC_X") ||
                        !AddPropertyChecked(req, planeId, _videoPlaneProps.CrtcYPropertyId, videoConfig.DstY, "CRTC_Y") ||
                        !AddPropertyChecked(req, planeId, _videoPlaneProps.CrtcWPropertyId, videoConfig.EffectiveDstWidth, "CRTC_W") ||
                        !AddPropertyChecked(req, planeId, _videoPlaneProps.CrtcHPropertyId, videoConfig.EffectiveDstHeight, "CRTC_H") ||
                        !AddPropertyChecked(req, planeId, _videoPlaneProps.SrcXPropertyId, 0, "SRC_X") ||
                        !AddPropertyChecked(req, planeId, _videoPlaneProps.SrcYPropertyId, 0, "SRC_Y") ||
                        !AddPropertyChecked(req, planeId, _videoPlaneProps.SrcWPropertyId, (ulong)videoConfig.SrcWidth << 16, "SRC_W") ||
                        !AddPropertyChecked(req, planeId, _videoPlaneProps.SrcHPropertyId, (ulong)videoConfig.SrcHeight << 16, "SRC_H"))
                    {
                        return (false, -1);
                    }

                    // Set zpos if available, requested, and NOT immutable
                    if (_videoPlaneProps.HasZpos() && _config.ZPos.HasValue && !_config.ZPos.Value.VideoZPosImmutable)
                    {
                        _logger?.LogDebug("Video plane {PlaneId} setting zpos={ZPos}", planeId, _config.ZPos.Value.VideoZPos);
                        if (!AddPropertyChecked(req, planeId, _videoPlaneProps.ZposPropertyId, _config.ZPos.Value.VideoZPos, "zpos"))
                        {
                            return (false, -1);
                        }
                    }
                    else if (_config.ZPos.HasValue)
                    {
                        _logger?.LogDebug("Video plane {PlaneId} zpos={ZPos} (immutable, not setting)", planeId, _config.ZPos.Value.VideoZPos);
                    }
                }

                // CRTC_ID and FB_ID must be set in every atomic commit
                if (!AddPropertyChecked(req, planeId, _videoPlaneProps.CrtcIdPropertyId, _config.CrtcId, "CRTC_ID") ||
                    !AddPropertyChecked(req, planeId, _videoPlaneProps.FbIdPropertyId, fbId, "FB_ID"))
                {
                    return (false, -1);
                }

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

                    if (!AddPropertyChecked(req, planeId, _osdPlaneProps.CrtcXPropertyId, osdConfig.DstX, "CRTC_X") ||
                        !AddPropertyChecked(req, planeId, _osdPlaneProps.CrtcYPropertyId, osdConfig.DstY, "CRTC_Y") ||
                        !AddPropertyChecked(req, planeId, _osdPlaneProps.CrtcWPropertyId, osdConfig.EffectiveDstWidth, "CRTC_W") ||
                        !AddPropertyChecked(req, planeId, _osdPlaneProps.CrtcHPropertyId, osdConfig.EffectiveDstHeight, "CRTC_H") ||
                        !AddPropertyChecked(req, planeId, _osdPlaneProps.SrcXPropertyId, 0, "SRC_X") ||
                        !AddPropertyChecked(req, planeId, _osdPlaneProps.SrcYPropertyId, 0, "SRC_Y") ||
                        !AddPropertyChecked(req, planeId, _osdPlaneProps.SrcWPropertyId, (ulong)osdConfig.SrcWidth << 16, "SRC_W") ||
                        !AddPropertyChecked(req, planeId, _osdPlaneProps.SrcHPropertyId, (ulong)osdConfig.SrcHeight << 16, "SRC_H"))
                    {
                        return (false, -1);
                    }

                    // Set zpos if available, requested, and NOT immutable
                    if (_osdPlaneProps.HasZpos() && _config.ZPos.HasValue && !_config.ZPos.Value.OsdZPosImmutable)
                    {
                        _logger?.LogDebug("OSD plane {PlaneId} setting zpos={ZPos}", planeId, _config.ZPos.Value.OsdZPos);
                        if (!AddPropertyChecked(req, planeId, _osdPlaneProps.ZposPropertyId, _config.ZPos.Value.OsdZPos, "zpos"))
                        {
                            return (false, -1);
                        }
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
                    if (!AddPropertyChecked(req, planeId, _osdPlaneProps.CrtcIdPropertyId, _config.CrtcId, "CRTC_ID") ||
                        !AddPropertyChecked(req, planeId, _osdPlaneProps.FbIdPropertyId, fbId, "FB_ID"))
                    {
                        return (false, -1);
                    }
                    _logger?.LogTrace("OSD plane {PlaneId}: CRTC_ID={CrtcId}, FB_ID={FbId}", planeId, _config.CrtcId, fbId);
                }
            }

            // Determine commit flags
            // Use ALLOW_MODESET on first plane initialization (blocking)
            // Use NONBLOCK for normal commits with fence return
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
                // Normal commit - use NONBLOCK with OUT_FENCE_PTR
                // Note: PAGE_FLIP_EVENT is NOT used because we rely on fence for synchronization.
                // Using PAGE_FLIP_EVENT without reading events via drmHandleEvent() causes -ENOMEM.
                flags = DrmModeAtomicFlags.DRM_MODE_ATOMIC_NONBLOCK;

                // Add OUT_FENCE_PTR property - kernel writes fence fd here
                // Use pointer to local variable (no fixed needed for stack-allocated int)
                int* fencePtr = &fenceFd;
                if (!AddPropertyChecked(req, _config.CrtcId, _modesetProps.OutFencePtrPropertyId,
                    (ulong)fencePtr, "OUT_FENCE_PTR"))
                {
                    return (false, -1);
                }
            }

            var result = LibDrm.drmModeAtomicCommit(_drmDevice.DeviceFd, req, flags, 0);

            if (result != 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                _logger?.LogTrace("Atomic commit failed: result={Result}, errno={Errno}", result, errno);
                return (false, -1);
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

            return (true, fenceFd);
        }
        finally
        {
            LibDrm.drmModeAtomicFree(req);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Stop commit thread (also closes fence fd)
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

        _commitSignal.Dispose();
        _cts.Dispose();

        _logger?.LogDebug("DualPlanePresenter2 disposed");
    }
}
