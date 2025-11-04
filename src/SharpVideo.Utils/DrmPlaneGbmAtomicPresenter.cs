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
public class DrmPlaneGbmAtomicPresenter : DrmSinglePlanePresenter
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
    /// </summary>
    public bool SubmitFrame()
    {
        // Lock the front buffer that was just rendered by EGL
        var newBo = LibGbm.LockFrontBuffer(_gbmSurface.Handle);
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
          LibGbm.ReleaseBuffer(_gbmSurface.Handle, newBo);
    return false;
        }

        // Queue for display (producer side)
        var queuedBuffer = new QueuedBuffer
   {
     Bo = newBo,
            FbId = fbId,
       Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

     _renderQueue.Enqueue(queuedBuffer);

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

         // If no flip in progress and we have a queued frame, start flip
  lock (_stateLock)
       {
   if (_initialized && !_flipInProgress && _renderQueue.TryDequeue(out var nextBuffer))
     {
       CommitAtomicFlip(nextBuffer);
   }
   }

  // Wait for page flip events
 var ret = Libc.poll(ref pollFd, 1, 16); // 16ms timeout (~60Hz)

       if (ret > 0 && (pollFd.revents & PollEvents.POLLIN) != 0)
     {
    unsafe
    {
              fixed (DrmEventContext* evctxPtr = &_eventContext)
     {
      var handleResult = LibDrm.drmHandleEvent(_drmDevice.DeviceFd, evctxPtr);
    if (handleResult < 0)
     {
        _logger.LogWarning("drmHandleEvent failed with error {Error}", handleResult);
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

    private static unsafe bool SetCrtcMode(
        DrmDevice drmDevice,
        uint crtcId,
        uint connectorId,
uint fbId,
    DrmModeInfo mode,
        uint width,
  uint height,
    ILogger logger)
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

        var nameBytes = System.Text.Encoding.UTF8.GetBytes(mode.Name);
        for (int i = 0; i < Math.Min(nameBytes.Length, 32); i++)
        {
      nativeMode.Name[i] = nameBytes[i];
      }

 var result = LibDrm.drmModeSetCrtc(drmDevice.DeviceFd, crtcId, fbId, 0, 0, &connectorId, 1, &nativeMode);
        if (result != 0)
        {
            logger.LogError("Failed to set CRTC mode: {Result}", result);
         return false;
   }

        logger.LogInformation("Successfully set CRTC to mode {Name}", mode.Name);
  return true;
    }

  public override void Cleanup()
    {
        base.Cleanup();

        _logger.LogInformation("Cleaning up atomic GBM presenter");

// Stop page flip thread
 _cts.Cancel();
 if (!_pageFlipThread.Join(TimeSpan.FromSeconds(2)))
        {
          _logger.LogWarning("Page flip thread did not stop gracefully");
        }

        // Clean up buffers
        lock (_stateLock)
        {
            if (_currentDisplayed.HasValue)
         {
   ReleaseBuffer(_currentDisplayed.Value);
    }

            if (_pendingFlip.HasValue)
            {
       ReleaseBuffer(_pendingFlip.Value);
      }

        while (_renderQueue.TryDequeue(out var buffer))
    {
        ReleaseBuffer(buffer);
            }

   // Clean up framebuffers
     foreach (var bufferInfo in _bufferCache.Values)
      {
 LibDrm.drmModeRmFB(_drmDevice.DeviceFd, bufferInfo.FbId);
 }
         _bufferCache.Clear();
    }

        // Dispose GBM surface
        _gbmSurface.Dispose();

        // Clean up GC handle
        if (_gcHandle.IsAllocated)
        {
 _gcHandle.Free();
        }

        _cts.Dispose();

     _logger.LogInformation("Atomic GBM presenter cleanup complete");
    }
}
