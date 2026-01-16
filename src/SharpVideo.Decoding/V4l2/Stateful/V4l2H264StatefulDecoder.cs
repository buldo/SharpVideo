using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Drm;
using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.V4L2;
using SharpVideo.Utils;
using SharpVideo.V4L2;

namespace SharpVideo.Decoding.V4l2.Stateful;

/// <summary>
/// V4L2 stateful H264 decoder.
/// Used for hardware decoders that manage decoding state internally
/// (e.g., Qualcomm Venus).
/// </summary>
/// <remarks>
/// Stateful decoders accept raw H264 NAL units (with start codes) and handle
/// all parsing, DPB management, and reference picture handling internally.
/// This is simpler than stateless decoders but offers less control.
/// </remarks>
[SupportedOSPlatform("linux")]
public class V4l2H264StatefulDecoder : BaseDecoder<SharedDmaBuffer>
{
    private readonly V4L2Device _device;
    private readonly V4l2DecoderConfiguration _configuration;
    private readonly DrmBufferManager _drmBufferManager;
    private readonly ILogger<V4l2H264StatefulDecoder> _logger;

    private List<SharedDmaBuffer>? _captureBuffers;
    private Dictionary<uint, SharedDmaBuffer>? _v4l2IndexToBuffer;

    private Thread? _captureThread;
    private CancellationTokenSource? _cts;
    private long _timestampCounter;
    private volatile bool _eosReceived;
    private TaskCompletionSource? _eosCompletionSource;

    private bool _isInitialized;
    private bool _streamingStarted;

    /// <inheritdoc />
    public override PixelFormat OutputPixelFormat { get; }

    private V4l2H264StatefulDecoder(
        V4L2Device device,
        V4l2DecoderConfiguration configuration,
        DrmBufferManager drmBufferManager,
        ILogger<V4l2H264StatefulDecoder> logger)
        : base(logger)
    {
        _device = device;
        _configuration = configuration;
        _drmBufferManager = drmBufferManager;
        _logger = logger;

        // Query the negotiated capture format for output pixel format
        var pixelFormat = _device.GetCaptureFormatMPlane().PixelFormat;
        OutputPixelFormat = new PixelFormat(pixelFormat);
    }

    /// <summary>
    /// Creates a new V4L2 stateful H264 decoder instance.
    /// </summary>
    /// <param name="device">The V4L2 device to use for decoding.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    /// <param name="configuration">Optional decoder configuration.</param>
    /// <param name="drmBufferManager">DRM buffer manager for zero-copy frame sharing.</param>
    /// <returns>A new stateful decoder instance.</returns>
    public static V4l2H264StatefulDecoder Create(
        V4L2Device device,
        ILoggerFactory loggerFactory,
        V4l2DecoderConfiguration? configuration,
        DrmBufferManager drmBufferManager)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(drmBufferManager);

        configuration ??= new V4l2DecoderConfiguration();
        var logger = loggerFactory.CreateLogger<V4l2H264StatefulDecoder>();

        return new V4l2H264StatefulDecoder(device, configuration, drmBufferManager, logger);
    }

    /// <inheritdoc />
    public override void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        _logger.LogInformation("Initializing V4L2 stateful H264 decoder...");

        // Subscribe to V4L2 events for dynamic resolution change and EOS handling
        SubscribeToEvents();

        // Configure formats
        ConfigureFormats();

        // Setup buffers
        SetupBuffers();

        _eosCompletionSource = new TaskCompletionSource();
        _isInitialized = true;

        _logger.LogInformation("V4L2 stateful H264 decoder initialized successfully");
    }

    private void SubscribeToEvents()
    {
        _logger.LogDebug("Subscribing to V4L2 events...");

        // Subscribe to SOURCE_CHANGE event for dynamic resolution changes
        var sourceChangeSubscription = new V4L2EventSubscription
        {
            Type = V4L2Constants.V4L2_EVENT_SOURCE_CHANGE,
            Id = 0,
            Flags = 0
        };
        var result = LibV4L2.SubscribeEvent(_device.fd, ref sourceChangeSubscription);
        if (!result.Success)
        {
            _logger.LogWarning("Failed to subscribe to SOURCE_CHANGE event: {Error}. " +
                "Dynamic resolution changes may not be handled correctly.", result.ErrorMessage);
        }
        else
        {
            _logger.LogDebug("Subscribed to SOURCE_CHANGE event");
        }

        // Subscribe to EOS event for end-of-stream handling
        var eosSubscription = new V4L2EventSubscription
        {
            Type = V4L2Constants.V4L2_EVENT_EOS,
            Id = 0,
            Flags = 0
        };
        result = LibV4L2.SubscribeEvent(_device.fd, ref eosSubscription);
        if (!result.Success)
        {
            _logger.LogWarning("Failed to subscribe to EOS event: {Error}. " +
                "End-of-stream detection may not work correctly.", result.ErrorMessage);
        }
        else
        {
            _logger.LogDebug("Subscribed to EOS event");
        }
    }

    private void ConfigureFormats()
    {
        _logger.LogDebug("Configuring stateful decoder formats...");

        // Configure OUTPUT format for H264 bitstream (with start codes for stateful)
        var outputFormat = new V4L2PixFormatMplane
        {
            Width = _configuration.InitialWidth,
            Height = _configuration.InitialHeight,
            PixelFormat = V4L2PixelFormats.V4L2_PIX_FMT_H264, // Stateful uses H264 with start codes
            NumPlanes = 1,
            Field = (uint)V4L2Field.NONE
        };
        _device.SetOutputFormatMPlane(outputFormat);

        var confirmedOutputFormat = _device.GetOutputFormatMPlane();
        _logger.LogInformation("Set OUTPUT format: {Width}x{Height} H264 ({Planes} plane(s))",
            confirmedOutputFormat.Width, confirmedOutputFormat.Height, confirmedOutputFormat.NumPlanes);

        // Query and set CAPTURE format - the driver negotiates the actual decoded format
        var captureFormat = _device.GetCaptureFormatMPlane();
        _logger.LogInformation("CAPTURE format: {Width}x{Height} FourCC=0x{FourCC:X8} ({Planes} plane(s))",
            captureFormat.Width, captureFormat.Height, captureFormat.PixelFormat, captureFormat.NumPlanes);
    }

    private void SetupBuffers()
    {
        _logger.LogDebug("Setting up buffers...");

        // Setup OUTPUT buffers for encoded data (MMAP)
        _device.OutputMPlaneQueue.InitMMap(_configuration.OutputBufferCount);
        foreach (var buffer in _device.OutputMPlaneQueue.BuffersPool.Buffers)
        {
            buffer.MapToMemory();
        }

        // Setup CAPTURE buffers for decoded frames (DMA-BUF)
        var captureFormat = _device.GetCaptureFormatMPlane();

        if (captureFormat.NumPlanes != 1)
        {
            throw new InvalidOperationException(
                $"Only single-plane DMABUF is supported, got {captureFormat.NumPlanes} planes");
        }

        _captureBuffers = _drmBufferManager.AllocateFromFormat(
            captureFormat.Width,
            captureFormat.Height,
            captureFormat.PlaneFormats[0],
            _configuration.CaptureBufferCount,
            new PixelFormat(captureFormat.PixelFormat));

        if (_captureBuffers.Count != _configuration.CaptureBufferCount)
        {
            throw new InvalidOperationException(
                $"Failed to allocate {_configuration.CaptureBufferCount} capture buffers, got {_captureBuffers.Count}");
        }

        // Initialize V4L2 DMABUF queue with the FDs
        var fds = _captureBuffers.Select(b => b.DmaBuffer.Fd).ToArray();
        _device.CaptureMPlaneQueue.InitDmaBuf(fds, captureFormat.PlaneFormats[0].SizeImage, 0u);

        // Build index mapping for fast dequeue lookup
        _v4l2IndexToBuffer = new Dictionary<uint, SharedDmaBuffer>();
        foreach (var buffer in _captureBuffers)
        {
            buffer.V4L2Buffer = _device.CaptureMPlaneQueue.DmaBufBuffersPool.Buffers
                .Single(b => b.DmaBufferFd == buffer.DmaBuffer.Fd);
            _v4l2IndexToBuffer[buffer.V4L2Buffer.Index] = buffer;
        }

        _logger.LogDebug("Allocated {Count} OUTPUT and {CaptureCount} CAPTURE buffers",
            _configuration.OutputBufferCount, _captureBuffers.Count);
    }

    private void EnsureStreamingStarted()
    {
        if (_streamingStarted)
        {
            return;
        }

        _logger.LogInformation("Starting V4L2 streaming...");

        // Queue all capture buffers before starting streaming
        _device.CaptureMPlaneQueue.EnqueueAllDmaBufBuffers();

        // Start streaming on both queues
        _device.OutputMPlaneQueue.StreamOn();
        _device.CaptureMPlaneQueue.StreamOn();

        // Start capture processing thread
        _cts = new CancellationTokenSource();
        _captureThread = new Thread(CaptureThreadLoop)
        {
            Name = "V4L2StatefulCaptureProcessor",
            IsBackground = true
        };
        _captureThread.Start();

        _streamingStarted = true;
        _logger.LogInformation("V4L2 streaming started");
    }

    /// <inheritdoc />
    public override void Decode(ReadOnlySpan<byte> nalu)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Decoder not initialized. Call Initialize() first.");
        }

        if (nalu.Length < 4)
        {
            return; // Too short to be a valid NALU
        }

        // Ensure we have a free buffer (blocks if all buffers are in use)
        _device.OutputMPlaneQueue.EnsureFreeBuffer();

        // Generate timestamp for frame ordering
        var timestamp = new TimeVal
        {
            TvSec = (nint)(Interlocked.Increment(ref _timestampCounter) / 1_000_000),
            TvUsec = (nint)(Interlocked.Read(ref _timestampCounter) % 1_000_000)
        };

        // Write NALU data and enqueue (no media request for stateful decoders)
        _device.OutputMPlaneQueue.WriteBufferAndEnqueue(nalu, request: null, timestamp);

        EnsureStreamingStarted();
    }

    private void CaptureThreadLoop()
    {
        _logger.LogInformation("Capture processing thread started");
        var cancellationToken = _cts!.Token;

        while (!cancellationToken.IsCancellationRequested)
        {
            // Check for events (SOURCE_CHANGE, EOS) using poll with POLLPRI
            // The WaitForReadyBuffer implementation uses poll internally
            CheckAndHandleEvents();

            // Wait for decoded frame - reduced timeout for lower latency (was 100ms)
            var dequeuedBuffer = _device.CaptureMPlaneQueue.WaitForReadyBuffer(16); // ~1 frame at 60fps
            if (dequeuedBuffer == null)
            {
                // Check if EOS was received and no more pending buffers
                if (_eosReceived)
                {
                    _logger.LogDebug("EOS received and no more buffers, signaling completion");
                    _eosCompletionSource?.TrySetResult();
                }
                continue;
            }

            // Find the SharedDmaBuffer by V4L2 buffer index
            if (!_v4l2IndexToBuffer!.TryGetValue(dequeuedBuffer.Index, out var decodedFrame))
            {
                _logger.LogWarning("Unknown buffer index {Index} dequeued", dequeuedBuffer.Index);
                continue;
            }

            _logger.LogTrace("Decoded frame ready: buffer index={Index}", dequeuedBuffer.Index);
            AddDecodedFrameToOutput(decodedFrame);
        }

        _logger.LogInformation("Capture processing thread stopped");
    }

    private void CheckAndHandleEvents()
    {
        // Try to dequeue any pending events
        var result = LibV4L2.DequeueEvent(_device.fd, out var @event);
        if (!result.Success)
        {
            // No events pending, which is normal
            return;
        }

        switch (@event.Type)
        {
            case V4L2Constants.V4L2_EVENT_SOURCE_CHANGE:
                HandleSourceChangeEvent(@event);
                break;

            case V4L2Constants.V4L2_EVENT_EOS:
                HandleEosEvent();
                break;

            default:
                _logger.LogDebug("Received unknown event type: {Type}", @event.Type);
                break;
        }
    }

    private void HandleSourceChangeEvent(V4L2Event @event)
    {
        var changes = @event.SourceChangeFlags;
        _logger.LogInformation("SOURCE_CHANGE event received: flags=0x{Flags:X}", changes);

        if ((changes & V4L2Constants.V4L2_EVENT_SRC_CH_RESOLUTION) != 0)
        {
            _logger.LogInformation("Resolution change detected, reconfiguring capture queue...");

            // TODO: Implement dynamic resolution change handling
            // Full implementation would require:
            // 1. Stop capture streaming
            // 2. Query new format from device
            // 3. Reallocate capture buffers with new dimensions
            // 4. Restart capture streaming
            //
            // For now, log warning and continue (decoder may produce incorrect frames)
            _logger.LogWarning("Dynamic resolution change not yet fully implemented. " +
                "Decoder may produce incorrect frames until reinitialized.");

            // Query new format to log the change
            var newFormat = _device.GetCaptureFormatMPlane();
            _logger.LogInformation("New CAPTURE format: {Width}x{Height} FourCC=0x{FourCC:X8}",
                newFormat.Width, newFormat.Height, newFormat.PixelFormat);
        }
    }

    private void HandleEosEvent()
    {
        _logger.LogInformation("EOS event received");
        _eosReceived = true;
    }

    /// <inheritdoc />
    public override void ReuseDecodedFrame(SharedDmaBuffer decodedFrame)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Decoder not initialized");
        }

        // Reset the buffer and requeue it for the decoder
        decodedFrame.V4L2Buffer.ResetPlanesUsed();
        _device.CaptureMPlaneQueue.ReuseDmaBufBuffer(decodedFrame.V4L2Buffer);

        _logger.LogTrace("Reused frame buffer: index={Index}", decodedFrame.V4L2Buffer.Index);
    }

    /// <inheritdoc />
    protected override void FlushDecoder()
    {
        _logger.LogInformation("Flushing decoder...");

        if (!_isInitialized || !_streamingStarted)
        {
            return;
        }

        // Send STOP command to signal end of stream and drain remaining frames
        var stopResult = LibV4L2.StopDecoder(_device.fd);
        if (!stopResult.Success)
        {
            _logger.LogWarning("Failed to send STOP command: {Error}", stopResult.ErrorMessage);
        }
        else
        {
            // Wait for EOS completion with timeout
            if (_eosCompletionSource != null)
            {
                var waitResult = _eosCompletionSource.Task.Wait(TimeSpan.FromSeconds(5));
                if (!waitResult)
                {
                    _logger.LogWarning("Timeout waiting for EOS completion");
                }
            }
        }

        _logger.LogDebug("Decoder flush completed");
    }

    private void Cleanup()
    {
        _logger.LogInformation("Cleaning up decoder resources...");

        // Stop capture thread
        if (_cts != null)
        {
            _cts.Cancel();
            if (_captureThread is { IsAlive: true })
            {
                if (!_captureThread.Join(TimeSpan.FromSeconds(2)))
                {
                    _logger.LogWarning("Capture thread did not stop gracefully");
                }
            }
            _cts.Dispose();
            _cts = null;
            _captureThread = null;
        }

        // Stop streaming
        if (_streamingStarted)
        {
            _device.OutputMPlaneQueue.StreamOff();
            _device.CaptureMPlaneQueue.StreamOff();
            _streamingStarted = false;
        }

        // Unsubscribe from events
        UnsubscribeFromEvents();

        // Unmap OUTPUT buffers
        foreach (var buffer in _device.OutputMPlaneQueue.BuffersPool.Buffers)
        {
            buffer.Unmap();
        }

        // Clear references
        _v4l2IndexToBuffer?.Clear();
        _captureBuffers?.Clear();

        _device.Dispose();

        _isInitialized = false;
        _logger.LogInformation("Decoder cleanup completed");
    }

    private void UnsubscribeFromEvents()
    {
        var sourceChangeSubscription = new V4L2EventSubscription
        {
            Type = V4L2Constants.V4L2_EVENT_SOURCE_CHANGE,
            Id = 0,
            Flags = 0
        };
        LibV4L2.UnsubscribeEvent(_device.fd, ref sourceChangeSubscription);

        var eosSubscription = new V4L2EventSubscription
        {
            Type = V4L2Constants.V4L2_EVENT_EOS,
            Id = 0,
            Flags = 0
        };
        LibV4L2.UnsubscribeEvent(_device.fd, ref eosSubscription);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Cleanup();
        }

        base.Dispose(disposing);
    }
}