using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Drm;
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
public class V4l2H264StatefulDecoder : BaseDecoder
{
    private readonly ILogger<V4l2H264StatefulDecoder> _logger;
    private readonly string _devicePath;
    private readonly V4l2DecoderConfiguration _configuration;
    private readonly DrmBufferManager? _drmBufferManager;

    private V4L2Device? _device;
    private List<SharedDmaBuffer>? _drmBuffers;

    // Thread for capture buffer processing
    private Thread? _captureThread;
    private CancellationTokenSource? _captureCts;

    private PixelFormat _outputPixelFormat;
    private bool _isInitialized;

    private V4l2H264StatefulDecoder(
        ILogger<V4l2H264StatefulDecoder> logger,
        string devicePath,
        V4l2DecoderConfiguration configuration,
        DrmBufferManager? drmBufferManager)
        : base(logger)
    {
        _logger = logger;
        _devicePath = devicePath;
        _configuration = configuration;
        _drmBufferManager = drmBufferManager;
        _outputPixelFormat = configuration.GetPixelFormat();
    }

    /// <summary>
    /// Creates a stateful H264 decoder using the specified device.
    /// </summary>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    /// <param name="decoderInfo">Decoder information from discovery.</param>
    /// <param name="configuration">Decoder configuration settings.</param>
    /// <param name="drmBufferManager">Optional DRM buffer manager for zero-copy decoding.</param>
    /// <returns>A new stateful decoder instance.</returns>
    public static V4l2H264StatefulDecoder Create(
        ILoggerFactory loggerFactory,
        V4l2H264DecoderInfo decoderInfo,
        V4l2DecoderConfiguration? configuration = null,
        DrmBufferManager? drmBufferManager = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(decoderInfo);

        if (decoderInfo.DecoderType != V4l2H264DecoderType.Stateful)
        {
            throw new ArgumentException(
                $"Expected stateful decoder info, got {decoderInfo.DecoderType}",
                nameof(decoderInfo));
        }

        configuration ??= new V4l2DecoderConfiguration();

        if (configuration.UseDrmPrimeBuffers && drmBufferManager == null)
        {
            throw new ArgumentException(
                "DrmBufferManager is required when UseDrmPrimeBuffers is true",
                nameof(drmBufferManager));
        }

        var logger = loggerFactory.CreateLogger<V4l2H264StatefulDecoder>();
        logger.LogInformation(
            "Creating V4L2 stateful H264 decoder at {DevicePath} ({Driver}: {Card})",
            decoderInfo.DevicePath,
            decoderInfo.Driver,
            decoderInfo.Card);

        return new V4l2H264StatefulDecoder(
            logger,
            decoderInfo.DevicePath,
            configuration,
            drmBufferManager);
    }

    /// <summary>
    /// Gets the device path used by this decoder.
    /// </summary>
    public string DevicePath => _devicePath;

    /// <inheritdoc />
    public override PixelFormat OutputPixelFormat => _outputPixelFormat;

    /// <inheritdoc />
    public override void Start()
    {
        if (!_isInitialized)
        {
            InitializeDecoder();
        }

        base.Start();
    }

    /// <inheritdoc />
    public override void Stop()
    {
        base.Stop();
        Cleanup();
    }

    /// <inheritdoc />
    public override void ReuseDecodedFrame(UniversalDecodedFrame decodedFrame)
    {
        if (decodedFrame is not V4l2DecodedFrame v4l2Frame)
        {
            throw new ArgumentException("Expected V4l2DecodedFrame", nameof(decodedFrame));
        }

        if (_device == null)
        {
            throw new InvalidOperationException("Decoder not initialized");
        }

        if (v4l2Frame.IsDmaBuf && v4l2Frame.DmaBuffer != null)
        {
            _device.CaptureMPlaneQueue.ReuseDmaBufBuffer(v4l2Frame.DmaBuffer.V4L2Buffer);
        }
        else
        {
            _device.CaptureMPlaneQueue.ReuseBuffer(v4l2Frame.BufferIndex);
        }
    }

    /// <inheritdoc />
    protected override void ProcessEncodedDataBuffer(UniversalEncodedBuffer encodedBuffer)
    {
        if (_device == null)
        {
            throw new InvalidOperationException("Decoder not initialized");
        }

        ReadOnlySpan<byte> naluData;

        if (encodedBuffer is V4l2EncodedBuffer v4l2Buffer)
        {
            naluData = v4l2Buffer.GetData();
        }
        else if (encodedBuffer is ManagedMemoryEncodedBuffer managedBuffer)
        {
            naluData = managedBuffer.Get();
        }
        else
        {
            _logger.LogWarning("Unsupported encoded buffer type: {Type}", encodedBuffer.GetType().Name);
            AddEncodedBufferToReuse(encodedBuffer);
            return;
        }

        if (naluData.Length < 1)
        {
            AddEncodedBufferToReuse(encodedBuffer);
            return;
        }

        // Stateful decoder: just submit the raw NALU with start code
        // The hardware handles all parsing and state management
        SubmitNaluToDevice(naluData);
        AddEncodedBufferToReuse(encodedBuffer);
    }

    /// <inheritdoc />
    protected override void FlushDecoder()
    {
        _logger.LogInformation("Flushing stateful decoder...");

        // For stateful decoders, we may need to send a special command
        // to flush the pipeline. This depends on the specific hardware.
        // Some decoders use V4L2_DEC_CMD_STOP
    }

    private void InitializeDecoder()
    {
        _logger.LogInformation("Initializing V4L2 stateful H264 decoder at {DevicePath}", _devicePath);

        // Open V4L2 device
        _device = V4L2DeviceFactory.Open(_devicePath);
        if (_device == null)
        {
            throw new InvalidOperationException($"Failed to open V4L2 device at {_devicePath}");
        }

        _logger.LogInformation("Device fd: {Fd}, Controls: {ControlCount}, ExtControls: {ExtControlCount}",
            _device.fd, _device.Controls.Count, _device.ExtendedControls.Count);

        // Configure formats
        ConfigureFormats();

        // Setup buffers
        SetupAndMapBuffers();

        // Start streaming
        StartStreaming();

        var outputFormat = _device.GetOutputFormatMPlane();
        var captureFormat = _device.GetCaptureFormatMPlane();
        _outputPixelFormat = new PixelFormat(captureFormat.PixelFormat);

        _logger.LogDebug("Streaming verification: Output {OutputFormat:X8}, Capture {CaptureFormat:X8}",
            outputFormat.PixelFormat, captureFormat.PixelFormat);

        _isInitialized = true;
        _logger.LogInformation("Stateful decoder initialization completed successfully");
    }

    private void ConfigureFormats()
    {
        _logger.LogInformation("Configuring stateful decoder formats...");

        // For stateful decoders, use V4L2_PIX_FMT_H264 (not H264_SLICE)
        var outputFormat = new V4L2PixFormatMplane
        {
            Width = _configuration.InitialWidth,
            Height = _configuration.InitialHeight,
            PixelFormat = V4L2PixelFormats.V4L2_PIX_FMT_H264,
            NumPlanes = 1,
            Field = (uint)V4L2Field.NONE,
            Colorspace = 5, // V4L2_COLORSPACE_REC709
            YcbcrEncoding = 1,
            Quantization = 1,
            XferFunc = 1
        };
        _device!.SetOutputFormatMPlane(outputFormat);

        var confirmedOutputFormat = _device.GetOutputFormatMPlane();
        _logger.LogInformation(
            "Set output format: {Width}x{Height} H264 ({Planes} plane(s))",
            confirmedOutputFormat.Width,
            confirmedOutputFormat.Height,
            confirmedOutputFormat.NumPlanes);

        var captureFormat = new V4L2PixFormatMplane
        {
            Width = _configuration.InitialWidth,
            Height = _configuration.InitialHeight,
            PixelFormat = _configuration.PreferredPixelFormat,
            NumPlanes = 2, // NV12 typically has 2 planes
            Field = (uint)V4L2Field.NONE,
            Colorspace = 5,
            YcbcrEncoding = 1,
            Quantization = 1,
            XferFunc = 1
        };
        _device.SetCaptureFormatMPlane(captureFormat);
    }

    private void SetupAndMapBuffers()
    {
        _logger.LogInformation("Setting up and mapping buffers...");

        // Setup OUTPUT buffers for encoded data
        SetupMMapBufferQueue(_device!.OutputMPlaneQueue, _configuration.OutputBufferCount);

        // Setup CAPTURE buffers for decoded frames
        if (_configuration.UseDrmPrimeBuffers)
        {
            SetupDmaBufCaptureQueue();
        }
        else
        {
            SetupMMapBufferQueue(_device.CaptureMPlaneQueue, _configuration.CaptureBufferCount);
        }
    }

    private void SetupMMapBufferQueue(V4L2DeviceQueue queue, uint bufferCount)
    {
        queue.InitMMap(bufferCount);
        foreach (var buffer in queue.BuffersPool.Buffers)
        {
            buffer.MapToMemory();
        }
    }

    private void SetupDmaBufCaptureQueue()
    {
        _logger.LogInformation("Setting up DMABUF capture queue with DRM PRIME buffers");
        var negotiatedFormat = _device!.GetCaptureFormatMPlane();

        if (negotiatedFormat.NumPlanes != 1)
        {
            throw new InvalidOperationException("Only 1 plane DMABUF is supported");
        }

        _drmBuffers = _drmBufferManager!.AllocateFromFormat(
            negotiatedFormat.Width,
            negotiatedFormat.Height,
            negotiatedFormat.PlaneFormats[0],
            _configuration.CaptureBufferCount,
            new PixelFormat(negotiatedFormat.PixelFormat));

        if (_drmBuffers.Count != _configuration.CaptureBufferCount)
        {
            throw new InvalidOperationException($"Failed to allocate {_configuration.CaptureBufferCount} DRM buffers");
        }

        var fds = _drmBuffers.Select(b => b.DmaBuffer.Fd).ToArray();
        _device.CaptureMPlaneQueue.InitDmaBuf(fds, negotiatedFormat.PlaneFormats[0].SizeImage, 0u);

        foreach (var buffer in _drmBuffers)
        {
            buffer.V4L2Buffer = _device.CaptureMPlaneQueue.DmaBufBuffersPool.Buffers
                .Single(b => b.DmaBufferFd == buffer.DmaBuffer.Fd);
        }
    }

    private void StartStreaming()
    {
        _logger.LogInformation("Starting V4L2 streaming...");

        if (_configuration.UseDrmPrimeBuffers)
        {
            _device!.CaptureMPlaneQueue.EnqueueAllDmaBufBuffers();
        }
        else
        {
            _device!.CaptureMPlaneQueue.EnqueueAllBuffers();
        }

        _device.OutputMPlaneQueue.StreamOn();
        _device.CaptureMPlaneQueue.StreamOn();

        _captureCts = new CancellationTokenSource();
        _captureThread = new Thread(ProcessCaptureBuffersThreadProc)
        {
            Name = "V4L2StatefulCaptureProcessor",
            IsBackground = true
        };
        _captureThread.Start();
        _logger.LogInformation("Started capture buffer processing thread");
    }

    private void ProcessCaptureBuffersThreadProc()
    {
        var cancellationToken = _captureCts!.Token;
        _logger.LogInformation("Capture buffer processing thread started");

        while (!cancellationToken.IsCancellationRequested)
        {
            var dequeuedBuffer = _device!.CaptureMPlaneQueue.WaitForReadyBuffer(1000);
            if (dequeuedBuffer == null)
            {
                continue;
            }

            V4l2DecodedFrame decodedFrame;
            var captureFormat = _device.GetCaptureFormatMPlane();

            if (_configuration.UseDrmPrimeBuffers)
            {
                decodedFrame = new V4l2DecodedFrame(_drmBuffers![(int)dequeuedBuffer.Index]);
            }
            else
            {
                var buffer = _device.CaptureMPlaneQueue.BuffersPool.Buffers[(int)dequeuedBuffer.Index];
                decodedFrame = new V4l2DecodedFrame(
                    buffer,
                    captureFormat.Width,
                    captureFormat.Height,
                    captureFormat.PlaneFormats[0].BytesPerLine,
                    new PixelFormat(captureFormat.PixelFormat));
            }

            AddDecodedFrameToOutput(decodedFrame);
        }

        _logger.LogInformation("Capture buffer processing thread stopped");
    }

    private void SubmitNaluToDevice(ReadOnlySpan<byte> naluData)
    {
        // For stateful decoders, we submit the raw NALU with start code
        // No media requests or extended controls needed
        _device!.OutputMPlaneQueue.EnsureFreeBuffer();
        _device.OutputMPlaneQueue.WriteBufferAndEnqueue(naluData, request: null);
    }

    private void Cleanup()
    {
        _logger.LogInformation("Cleaning up decoder resources...");

        if (_captureCts != null)
        {
            _captureCts.Cancel();

            if (_captureThread is { IsAlive: true })
            {
                if (!_captureThread.Join(TimeSpan.FromSeconds(2)))
                {
                    _logger.LogWarning("Capture thread did not stop gracefully");
                }
            }

            _captureCts.Dispose();
            _captureCts = null;
            _captureThread = null;
        }

        if (_device != null)
        {
            _device.OutputMPlaneQueue.StreamOff();
            _device.CaptureMPlaneQueue.StreamOff();

            UnmapBuffers(_device.OutputMPlaneQueue);

            if (!_configuration.UseDrmPrimeBuffers)
            {
                UnmapBuffers(_device.CaptureMPlaneQueue);
            }

            _device.Dispose();
            _device = null;
        }

        _isInitialized = false;
        _logger.LogInformation("Decoder cleanup completed");
    }

    private void UnmapBuffers(V4L2DeviceQueue queue)
    {
        foreach (var buffer in queue.BuffersPool.Buffers)
        {
            buffer.Unmap();
        }
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