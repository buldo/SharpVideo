using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.C;
using SharpVideo.Linux.Native.Gbm;

namespace SharpVideo.Utils;

/// <summary>
/// Atomic DRM plane presenter using GBM buffers for OpenGL ES rendering.
/// Implements two-threaded architecture:
/// - Render thread: renders at maximum FPS without vsync blocking
/// - Page flip thread: handles vblank-synchronized display updates
/// </summary>
[SupportedOSPlatform("linux")]
public class DrmPlaneGbmAtomicPresenter : DrmSinglePlanePresenter, IDisposable
{
    private readonly GbmDevice _gbmDevice;
    private readonly GbmSurface _gbmSurface;
    private readonly uint _width;
    private readonly uint _height;
  private readonly PixelFormat _format;
    private readonly ILogger _logger;
    private readonly uint _crtcId;
    private readonly uint _connectorId;
  private readonly DrmModeInfo _mode;

    // Thread synchronization
    private readonly Thread _pageFlipThread;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _stateLock = new();
    private readonly GCHandle _gcHandle;

  // Atomic properties
    private readonly AtomicPlaneProperties _props;

    // Buffer management
    private readonly ConcurrentQueue<QueuedBuffer> _renderQueue = new();
    private QueuedBuffer? _currentDisplayed;
    private QueuedBuffer? _pendingFlip;
    private bool _flipInProgress;
    private bool _initialized;

    // Event handling
    private readonly LibDrm.DrmEventPageFlipHandler _pageFlipHandler;
    private DrmEventContext _eventContext;

    // Buffer tracking
    private readonly Dictionary<nint, BufferInfo> _bufferCache = new();

    private struct QueuedBuffer
    {
        public nint Bo;
        public uint FbId;
        public long Timestamp; // for metrics
    }

    private struct BufferInfo
    {
 public uint FbId;
     public uint Handle;
    }

    public DrmPlaneGbmAtomicPresenter(
        DrmDevice drmDevice,
        DrmPlane plane,
        uint crtcId,
        uint width,
        uint height,
        ILogger logger,
        GbmDevice gbmDevice,
        PixelFormat format,
        uint connectorId,
        DrmModeInfo mode)
    : base(drmDevice, plane, crtcId, width, height, logger)
    {
        // Verify atomic mode is supported - fail fast if not
        if (!drmDevice.TrySetClientCapability(DrmClientCapability.DRM_CLIENT_CAP_ATOMIC, true, out var result))
        {
            var errno = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            throw new NotSupportedException(
                $"Atomic modesetting is not supported by this DRM device. " +
                $"Error code: {result}, errno: {errno}. " +
                $"Use DrmPlaneGbmPresenter (legacy mode) instead.");
        }

        _gbmDevice = gbmDevice;
        _width = width;
        _height = height;
        _format = format;
        _logger = logger;
        _crtcId = crtcId;
        _connectorId = connectorId;
        _mode = mode;

        _logger.LogInformation("Creating GBM surface for atomic presenter: {Width}x{Height} {Format}",
          width, height, format.GetName());

        // Create GBM surface for scanout and rendering
        _gbmSurface = _gbmDevice.CreateSurface(
            width,
            height,
            format,
            GbmBoUse.GBM_BO_USE_SCANOUT | GbmBoUse.GBM_BO_USE_RENDERING);

        _logger.LogInformation("GBM surface created successfully");

        // Get atomic properties
        _props = GetAtomicProperties();

        // Setup page flip event handling
        _pageFlipHandler = OnPageFlipComplete;
        _gcHandle = GCHandle.Alloc(_pageFlipHandler);

        _eventContext = new DrmEventContext
        {
            version = LibDrm.DRM_EVENT_CONTEXT_VERSION,
            page_flip_handler = Marshal.GetFunctionPointerForDelegate(_pageFlipHandler)
        };

        // Start page flip thread
        _pageFlipThread = new Thread(PageFlipThreadLoop)
        {
            Name = "DRM Page Flip Thread",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _pageFlipThread.Start();

        _logger.LogInformation("Atomic GBM presenter initialized with two-threaded architecture");
    }

    /// <summary>
    /// Gets the native GBM surface handle for EGL context creation.
    /// </summary>
    public nint GetNativeGbmSurfaceHandle()
    {
        return _gbmSurface.Handle;
    }

    /// <summary>
    /// Submits a rendered frame for display. Non-blocking call that returns immediately.
    /// The frame will be displayed at the next vblank by the page flip thread.
    /// Called from render thread after eglSwapBuffers().
    /// 
    /// Returns false if a frame is already queued (frame should be dropped to maintain max FPS).
    /// </summary>
    public bool SubmitFrame()
    {
        nint newBo = 0;
        bool shouldRelease = false;
        
        try
        {
            // Lock the front buffer that was just rendered by EGL
            newBo = LibGbm.LockFrontBuffer(_gbmSurface.Handle);
            if (newBo == 0)
            {
                _logger.LogError("Failed to lock front buffer from GBM surface");
                return false;
            }

            // Get or create framebuffer for this BO
            var fbId = GetOrCreateFramebuffer(newBo);
            if (fbId == 0)
            {
                _logger.LogError("Failed to get framebuffer for BO");
                shouldRelease = true; // Mark for cleanup
                return false;
            }

            // Queue for display (producer side)
            var queuedBuffer = new QueuedBuffer
            {
                Bo = newBo,
                FbId = fbId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            // Atomically check state and enqueue - ALL operations under single lock
            lock (_stateLock)
            {
                // If queue is not empty or flip is in progress, drop this frame
                // This prevents buffer exhaustion when rendering faster than display refresh
                if (!_renderQueue.IsEmpty || _flipInProgress)
                {
                    // Frame dropped - need to release buffer before returning
                    shouldRelease = true;
                    return false;
                }

                // Safe to enqueue - transfer ownership to queue
                _renderQueue.Enqueue(queuedBuffer);
                newBo = 0; // Clear to indicate ownership transferred
            }

            // If this is the first frame, trigger initialization
            if (!_initialized)
            {
                lock (_stateLock)
                {
                    if (!_initialized)
                    {
                        _logger.LogInformation("First frame submitted, will initialize DRM display in page flip thread");
                    }
                }
            }

            return true;
        }
        finally
        {
            // Clean up buffer if we still own it (failed to enqueue or error occurred)
            // This check is safe because we cleared newBo=0 after successful enqueue
            if (shouldRelease && newBo != 0)
            {
                LibGbm.ReleaseBuffer(_gbmSurface.Handle, newBo);
            }
        }
    }

    private AtomicPlaneProperties GetAtomicProperties()
    {
var props = new AtomicPlaneProperties(_plane);

        if (props.FbIdPropertyId == 0 || props.CrtcIdPropertyId == 0)
        {
throw new Exception("Failed to get required plane properties for atomic modesetting");
        }

        _logger.LogDebug("Atomic plane properties retrieved successfully");
        return props;
    }

    private uint GetOrCreateFramebuffer(nint bo)
    {
        // Check cache first
        if (_bufferCache.TryGetValue(bo, out var bufferInfo))
        {
    return bufferInfo.FbId;
    }

        // Create new framebuffer
        var width = LibGbm.GetWidth(bo);
        var height = LibGbm.GetHeight(bo);
   var stride = LibGbm.GetStride(bo);
        var handle = LibGbm.GetHandle(bo);

        if (handle == 0)
        {
  _logger.LogError("Failed to get BO handle");
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
    width,
                height,
     _format.Fourcc,
 handles,
                pitches,
       offsets,
       out var fbId,
        0);

            if (result != 0)
            {
   _logger.LogError("drmModeAddFB2 failed with error {Error}", result);
        return 0;
      }

  // Cache it
            _bufferCache[bo] = new BufferInfo { FbId = fbId, Handle = handle };

            _logger.LogTrace("Created framebuffer ID {FbId} for BO 0x{Bo:X}", fbId, bo);
            return fbId;
        }
    }

    private void PageFlipThreadLoop()
    {
        _logger.LogInformation("Page flip thread started");

        var pollFd = new PollFd
        {
            fd = _drmDevice.DeviceFd,
            events = PollEvents.POLLIN
        };

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                // Check if we have a new frame to display
                if (!_initialized && _renderQueue.TryDequeue(out var firstBuffer))
                {
                    // Initialize display with first frame
                    InitializeDisplay(firstBuffer);
                    continue;
                }

                // Check if we can start a new flip and commit atomically under single lock
                lock (_stateLock)
                {
                    if (_initialized && !_flipInProgress)
                    {
                        if (_renderQueue.TryDequeue(out var nextBuffer))
                        {
                            // Commit inside the same lock to ensure atomicity
                            CommitAtomicFlip(nextBuffer);
                        }
                    }
                }

                // Wait for page flip events
                var timeout = Math.Max(5, (int)(1000.0 / _mode.VRefresh * 0.9)); // Dynamic timeout based on refresh rate
                var ret = Libc.poll(ref pollFd, 1, timeout);

                if (ret > 0 && (pollFd.revents & PollEvents.POLLIN) != 0)
                {
                    unsafe
                    {
                        fixed (DrmEventContext* evctxPtr = &_eventContext)
                        {
                            var handleResult = LibDrm.drmHandleEvent(_drmDevice.DeviceFd, evctxPtr);
                            if (handleResult < 0)
                            {
                                var errno = Marshal.GetLastPInvokeError();
                                _logger.LogWarning("drmHandleEvent failed with error {Error}, errno {Errno}", 
                                    handleResult, errno);
                            }
                        }
                    }
                }
                else if (ret < 0)
                {
                    var errno = Marshal.GetLastPInvokeError();
                    if (errno != 4) // EINTR
                    {
                        _logger.LogWarning("poll() failed with errno {Errno}", errno);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in page flip thread");
            }
        }

        _logger.LogInformation("Page flip thread stopped");
    }

    private void InitializeDisplay(QueuedBuffer buffer)
    {
        lock (_stateLock)
        {
if (_initialized) return;

       _logger.LogInformation("Initializing DRM display with first frame");

    // Set CRTC mode using legacy API for initialization
    if (!SetCrtcMode(_drmDevice, _crtcId, _connectorId, buffer.FbId, _mode, _width, _height, _logger))
            {
                _logger.LogError("Failed to set CRTC mode during initialization");
         ReleaseBuffer(buffer);
  return;
            }

 _currentDisplayed = buffer;
            _initialized = true;

     _logger.LogInformation("DRM display initialized successfully");
        }
    }

  private unsafe void CommitAtomicFlip(QueuedBuffer buffer)
    {
 // Must be called under _stateLock
        var req = LibDrm.drmModeAtomicAlloc();
        if (req == null)
        {
   _logger.LogError("Failed to allocate atomic request");
          ReleaseBuffer(buffer);
          return;
   }

      try
        {
    int ret;

   ret = LibDrm.drmModeAtomicAddProperty(req, _plane.Id, _props.FbIdPropertyId, buffer.FbId);
      if (ret < 0) goto error;

ret = LibDrm.drmModeAtomicAddProperty(req, _plane.Id, _props.CrtcIdPropertyId, _crtcId);
 if (ret < 0) goto error;

            ret = LibDrm.drmModeAtomicAddProperty(req, _plane.Id, _props.CrtcXPropertyId, 0);
            if (ret < 0) goto error;

       ret = LibDrm.drmModeAtomicAddProperty(req, _plane.Id, _props.CrtcYPropertyId, 0);
       if (ret < 0) goto error;

  ret = LibDrm.drmModeAtomicAddProperty(req, _plane.Id, _props.CrtcWPropertyId, _width);
            if (ret < 0) goto error;

ret = LibDrm.drmModeAtomicAddProperty(req, _plane.Id, _props.CrtcHPropertyId, _height);
     if (ret < 0) goto error;

            ret = LibDrm.drmModeAtomicAddProperty(req, _plane.Id, _props.SrcXPropertyId, 0);
          if (ret < 0) goto error;

    ret = LibDrm.drmModeAtomicAddProperty(req, _plane.Id, _props.SrcYPropertyId, 0);
   if (ret < 0) goto error;

       ret = LibDrm.drmModeAtomicAddProperty(req, _plane.Id, _props.SrcWPropertyId, _width << 16);
            if (ret < 0) goto error;

   ret = LibDrm.drmModeAtomicAddProperty(req, _plane.Id, _props.SrcHPropertyId, _height << 16);
      if (ret < 0) goto error;

  var flags = DrmModeAtomicFlags.DRM_MODE_ATOMIC_NONBLOCK |
    DrmModeAtomicFlags.DRM_MODE_PAGE_FLIP_EVENT;

    ret = LibDrm.drmModeAtomicCommit(_drmDevice.DeviceFd, req, flags, 0);
  if (ret == 0)
      {
      _flipInProgress = true;
     _pendingFlip = buffer;
     return;
     }

            _logger.LogWarning("Atomic commit failed with error {Error}, dropping frame", ret);
   ReleaseBuffer(buffer);
            return;

error:
    _logger.LogError("Failed to add atomic property");
       ReleaseBuffer(buffer);
      }
        finally
        {
     LibDrm.drmModeAtomicFree(req);
        }
    }

  private void OnPageFlipComplete(int fd, uint sequence, uint tv_sec, uint tv_usec, nint user_data)
    {
     lock (_stateLock)
     {
   _flipInProgress = false;

   // Release the previously displayed buffer
            if (_currentDisplayed.HasValue)
            {
                ReleaseBuffer(_currentDisplayed.Value);
       }

            // The pending flip is now displayed
            _currentDisplayed = _pendingFlip;
            _pendingFlip = null;

     // Immediately start next flip if we have queued frames
        if (_renderQueue.TryDequeue(out var nextBuffer))
    {
        CommitAtomicFlip(nextBuffer);
}
        }
    }

    private void ReleaseBuffer(QueuedBuffer buffer)
    {
        LibGbm.ReleaseBuffer(_gbmSurface.Handle, buffer.Bo);
    }

    public override void Cleanup()
    {
        Dispose();
    }

    private bool _disposed;

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        _logger.LogInformation("Disposing atomic GBM presenter");

        try
        {
            // Step 1: Stop event loop thread FIRST to prevent race with page flip events
            _cts.Cancel();
            
            if (!_pageFlipThread.Join(TimeSpan.FromSeconds(2)))
            {
                _logger.LogWarning("Page flip thread did not stop gracefully within 2 seconds, aborting");
#pragma warning disable SYSLIB0006 // Thread.Abort is obsolete but needed for cleanup
                try
                {
                    _pageFlipThread.Interrupt();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to abort page flip thread");
                }
#pragma warning restore SYSLIB0006
            }

            // Step 2: Now safe to disable plane (no more events can arrive)
            try
            {
                LibDrm.drmModeSetPlane(_drmDevice.DeviceFd, _plane.Id, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                _logger.LogDebug("Plane disabled successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to disable plane during cleanup");
            }

            // Step 3: Clean up buffers under lock (thread is stopped, safe to access)
            lock (_stateLock)
            {
                if (_currentDisplayed.HasValue)
                {
                    ReleaseBuffer(_currentDisplayed.Value);
                    _currentDisplayed = null;
                }

                if (_pendingFlip.HasValue)
                {
                    ReleaseBuffer(_pendingFlip.Value);
                    _pendingFlip = null;
                }

                while (_renderQueue.TryDequeue(out var buffer))
                {
                    ReleaseBuffer(buffer);
                }

                // Step 4: Clean up framebuffers
                foreach (var bufferInfo in _bufferCache.Values)
                {
                    try
                    {
                        LibDrm.drmModeRmFB(_drmDevice.DeviceFd, bufferInfo.FbId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to remove framebuffer ID {FbId}", bufferInfo.FbId);
                    }
                }
                _bufferCache.Clear();
            }

            // Step 5: Dispose GBM surface
            if (disposing)
            {
                try
                {
                    _gbmSurface.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to dispose GBM surface");
                }
            }

            // Step 6: Clean up GC handle
            if (_gcHandle.IsAllocated)
            {
                _gcHandle.Free();
            }

            // Step 7: Dispose cancellation token source
            if (disposing)
            {
                _cts.Dispose();
            }

            _logger.LogInformation("Atomic GBM presenter cleanup complete");
        }
        finally
        {
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~DrmPlaneGbmAtomicPresenter()
    {
        Dispose(false);
    }
}
