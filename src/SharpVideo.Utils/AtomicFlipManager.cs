using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.C;

namespace SharpVideo.Utils;

/// <summary>
/// Manages atomic modesetting with VBlank-synchronized display.
/// Implements a latency-optimized algorithm that always displays the latest available frame.
/// </summary>
[SupportedOSPlatform("linux")]
public unsafe class AtomicFlipManager : IDisposable
{
    private readonly DrmDevice _drmDevice;
    private readonly DrmPlane _drmPlane;
    private readonly uint _crtcId;
    private readonly AtomicPlaneProperties _props;
    private readonly uint _srcWidth;
    private readonly uint _srcHeight;
    private readonly uint _dstWidth;
    private readonly uint _dstHeight;
    private readonly ILogger _logger;
    private readonly Thread _eventThread;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();
    private readonly GCHandle _gcHandle;

    // Frame tracking state
    private uint _latestFbId;
    private SharedDmaBuffer? _latestBuffer;
    private bool _flipPending;
    private readonly Queue<SharedDmaBuffer> _completedBuffers = new();

    // Event handling
    private readonly LibDrm.DrmEventPageFlipHandler _pageFlipHandler;
    private DrmEventContext _eventContext;

    public AtomicFlipManager(
        DrmDevice drmDevice,
        DrmPlane drmPlane,
        uint crtcId,
        AtomicPlaneProperties props,
        uint srcWidth,
        uint srcHeight,
        uint dstWidth,
        uint dstHeight,
        ILogger logger)
    {
        _drmDevice = drmDevice;
        _drmPlane = drmPlane;
        _crtcId = crtcId;
        _props = props;
        _srcWidth = srcWidth;
        _srcHeight = srcHeight;
        _dstWidth = dstWidth;
        _dstHeight = dstHeight;
        _logger = logger;

        // Create delegate and pin it
        _pageFlipHandler = PageFlipHandler;
        _gcHandle = GCHandle.Alloc(_pageFlipHandler);

        // Setup event context
        _eventContext = new DrmEventContext
        {
            version = LibDrm.DRM_EVENT_CONTEXT_VERSION,
            page_flip_handler = Marshal.GetFunctionPointerForDelegate(_pageFlipHandler)
        };

        // Start event loop thread
        _eventThread = new Thread(EventLoop)
        {
            Name = "DRM Event Loop",
            IsBackground = true
        };
        _eventThread.Start();

        _logger.LogInformation("Atomic display manager initialized with VBlank synchronization");
    }

    /// <summary>
    /// Submit a new decoded frame for display.
    /// The frame will be shown at the next VBlank if no flip is pending,
    /// or queued as the latest frame if a flip is in progress.
    /// </summary>
    public void SubmitFrame(SharedDmaBuffer buffer, uint fbId)
    {
        lock (_lock)
        {
            // Always save the latest frame
            if (_latestBuffer != null && _latestBuffer != buffer)
            {
                // Previous latest frame was never displayed, return it immediately
                _completedBuffers.Enqueue(_latestBuffer);
            }

            _latestFbId = fbId;
            _latestBuffer = buffer;

            // If no flip is pending, commit immediately
            if (!_flipPending)
            {
                CommitFrame(fbId);
            }
            // Otherwise, the frame will be picked up by the page flip handler
        }
    }

    /// <summary>
    /// Get all buffers that have been presented and are ready to be reused.
    /// </summary>
    public SharedDmaBuffer[] GetCompletedBuffers()
    {
        lock (_lock)
        {
            var result = _completedBuffers.ToArray();
            _completedBuffers.Clear();
            return result;
        }
    }

    private void CommitFrame(uint fbId)
    {
        // This method must be called under lock
        var req = LibDrm.drmModeAtomicAlloc();
        if (req == null)
        {
            _logger.LogError("Failed to allocate atomic request");
            return;
        }

        try
        {
            // Add all required plane properties
            int ret;

            ret = LibDrm.drmModeAtomicAddProperty(req, _drmPlane.Id, _props.FbIdPropertyId, fbId);
            if (ret < 0)
            {
                _logger.LogError("Failed to add FB_ID property");
                return;
            }

            ret = LibDrm.drmModeAtomicAddProperty(req, _drmPlane.Id, _props.CrtcIdPropertyId, _crtcId);
            if (ret < 0)
            {
                _logger.LogError("Failed to add CRTC_ID property");
                return;
            }

            // Position on CRTC (destination)
            ret = LibDrm.drmModeAtomicAddProperty(req, _drmPlane.Id, _props.CrtcXPropertyId, 0);
            if (ret < 0)
            {
                _logger.LogError("Failed to add CRTC_X property");
                return;
            }

            ret = LibDrm.drmModeAtomicAddProperty(req, _drmPlane.Id, _props.CrtcYPropertyId, 0);
            if (ret < 0)
            {
                _logger.LogError("Failed to add CRTC_Y property");
                return;
            }

            ret = LibDrm.drmModeAtomicAddProperty(req, _drmPlane.Id, _props.CrtcWPropertyId, _dstWidth);
            if (ret < 0)
            {
                _logger.LogError("Failed to add CRTC_W property");
                return;
            }

            ret = LibDrm.drmModeAtomicAddProperty(req, _drmPlane.Id, _props.CrtcHPropertyId, _dstHeight);
            if (ret < 0)
            {
                _logger.LogError("Failed to add CRTC_H property");
                return;
            }

            // Source rectangle in framebuffer (16.16 fixed point)
            ret = LibDrm.drmModeAtomicAddProperty(req, _drmPlane.Id, _props.SrcXPropertyId, 0);
            if (ret < 0)
            {
                _logger.LogError("Failed to add SRC_X property");
                return;
            }

            ret = LibDrm.drmModeAtomicAddProperty(req, _drmPlane.Id, _props.SrcYPropertyId, 0);
            if (ret < 0)
            {
                _logger.LogError("Failed to add SRC_Y property");
                return;
            }

            ret = LibDrm.drmModeAtomicAddProperty(req, _drmPlane.Id, _props.SrcWPropertyId, _srcWidth << 16);
            if (ret < 0)
            {
                _logger.LogError("Failed to add SRC_W property");
                return;
            }

            ret = LibDrm.drmModeAtomicAddProperty(req, _drmPlane.Id, _props.SrcHPropertyId, _srcHeight << 16);
            if (ret < 0)
            {
                _logger.LogError("Failed to add SRC_H property");
                return;
            }

            // Commit with NONBLOCK and PAGE_FLIP_EVENT
            var flags = DrmModeAtomicFlags.DRM_MODE_ATOMIC_NONBLOCK |
                       DrmModeAtomicFlags.DRM_MODE_PAGE_FLIP_EVENT;

            ret = LibDrm.drmModeAtomicCommit(_drmDevice.DeviceFd, req, flags, 0);
            if (ret == 0)
            {
                _flipPending = true;
            }
            else
            {
                _logger.LogWarning("Atomic commit failed with error {Error}", ret);
            }
        }
        finally
        {
            LibDrm.drmModeAtomicFree(req);
        }
    }

    private void PageFlipHandler(int fd, uint sequence, uint tv_sec, uint tv_usec, nint user_data)
    {
        lock (_lock)
        {
            _flipPending = false;

            // Move the currently displayed buffer to completed queue
            if (_latestBuffer != null)
            {
                _completedBuffers.Enqueue(_latestBuffer);
                _latestBuffer = null;
            }

            // Immediately schedule next flip with the latest frame if available
            if (_latestFbId != 0)
            {
                CommitFrame(_latestFbId);
            }
        }
    }

    private void EventLoop()
    {
        _logger.LogInformation("DRM event loop started");

        var pollFd = new PollFd
        {
            fd = _drmDevice.DeviceFd,
            events = PollEvents.POLLIN
        };

        while (!_cts.Token.IsCancellationRequested)
        {
            var ret = Libc.poll(ref pollFd, 1, 100); // 100ms timeout

            if (ret > 0 && (pollFd.revents & PollEvents.POLLIN) != 0)
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
            else if (ret < 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                if (errno != 4) // EINTR
                {
                    _logger.LogError("poll() failed with errno {Errno}", errno);
                }
            }
        }

        _logger.LogInformation("DRM event loop stopped");
    }

    public void Dispose()
    {
        _cts.Cancel();

        if (!_eventThread.Join(TimeSpan.FromSeconds(2)))
        {
            _logger.LogWarning("Event thread did not stop gracefully");
        }

        _cts.Dispose();

        if (_gcHandle.IsAllocated)
        {
            _gcHandle.Free();
        }
    }
}
