using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Utils;
using SharpVideo.V4L2;
using SharpVideo.V4L2Decoding.Models;

namespace SharpVideo.V4L2Decoding.Services;

/// <summary>
/// Base class for V4L2 H.264 decoders providing common buffer management and streaming functionality
/// </summary>
[SupportedOSPlatform("linux")]
public abstract class H264V4L2DecoderBase : IAsyncDisposable
{
    protected readonly V4L2Device Device;
    protected readonly ILogger Logger;
    protected readonly DecoderConfiguration Configuration;
    protected readonly DrmBufferManager? DrmBufferManager;

    protected Action<ReadOnlySpan<byte>>? ProcessDecodedAction;
    protected Action<SharedDmaBuffer>? ProcessDecodedBufferIndex;
    protected List<SharedDmaBuffer>? DrmBuffers;

    protected bool Disposed;
    protected int FramesDecoded;

    // Thread for processing capture buffers
    protected Thread? CaptureThread;
    protected CancellationTokenSource? CaptureThreadCts;

    protected H264V4L2DecoderBase(
        V4L2Device device,
        ILogger logger,
        DecoderConfiguration configuration,
        Action<ReadOnlySpan<byte>>? processDecodedAction,
        DrmBufferManager? drmBufferManager)
    {
        Device = device;
        Logger = logger;
        Configuration = configuration;
        ProcessDecodedAction = processDecodedAction;
        DrmBufferManager = drmBufferManager;

        if (Configuration.UseDrmPrimeBuffers && DrmBufferManager == null)
        {
            throw new ArgumentException("DrmBufferManager is required when UseDrmPrimeBuffers is true");
        }
    }

    public H264V4L2DecoderStatistics Statistics { get; } = new();

    /// <summary>
    /// Configure output and capture formats - to be implemented by derived classes
    /// </summary>
    protected abstract void ConfigureFormats();

    /// <summary>
    /// Setup decoder-specific controls - to be implemented by derived classes
    /// </summary>
    protected abstract void ConfigureDecoderControls();

    /// <summary>
    /// Decodes H.264 stream - to be implemented by derived classes
    /// </summary>
    public abstract Task DecodeStreamAsync(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Setup decoder-specific buffer configuration - can be overridden by derived classes
    /// </summary>
    protected virtual void SetupDecoderBuffers()
    {
        // Default implementation: setup OUTPUT buffers
        SetupMMapBufferQueue(Device.OutputMPlaneQueue, Configuration.OutputBufferCount);

        // Setup CAPTURE buffers for decoded frames
        if (Configuration.UseDrmPrimeBuffers)
        {
            SetupDmaBufCaptureQueue();
        }
        else
        {
            SetupMMapBufferQueue(Device.CaptureMPlaneQueue, Configuration.CaptureBufferCount);
        }
    }

    /// <summary>
    /// Configures the V4L2 decoder formats and buffers
    /// </summary>
    public virtual void InitializeDecoder(Action<SharedDmaBuffer>? processDecodedBufferIndex)
    {
        Logger.LogInformation("Initializing H.264 decoder...");

        ProcessDecodedBufferIndex = processDecodedBufferIndex;
        if (Configuration.UseDrmPrimeBuffers && ProcessDecodedBufferIndex == null)
        {
            throw new ArgumentException(
                "processDecodedBufferIndex callback is required when UseDrmPrimeBuffers is true");
        }

        // Log device information for debugging
        Logger.LogInformation("Device fd: {Fd}, Controls: {ControlCount}, ExtControls: {ExtControlCount}",
            Device.fd, Device.Controls.Count, Device.ExtendedControls.Count);

        // Configure decoder formats
        ConfigureFormats();

        // Configure decoder-specific controls
        ConfigureDecoderControls();

        // Setup and map buffers
        SetupDecoderBuffers();

        // Start streaming on both queues
        StartStreaming();

        // Verify streaming is actually working
        var outputFormat = Device.GetOutputFormatMPlane();
        var captureFormat = Device.GetCaptureFormatMPlane();

        Logger.LogDebug("Streaming verification: Output {OutputFormat:X8}, Capture {CaptureFormat:X8}",
            outputFormat.PixelFormat, captureFormat.PixelFormat);

        Logger.LogInformation("Decoder initialization completed successfully");
    }

    /// <summary>
    /// Setup and map buffers for both output and capture queues
    /// </summary>
    [Obsolete("Use SetupDecoderBuffers instead")]
    protected virtual void SetupAndMapBuffers()
    {
        SetupDecoderBuffers();
    }

    /// <summary>
    /// Setup MMAP buffer queue
    /// </summary>
    protected void SetupMMapBufferQueue(V4L2DeviceQueue queue, uint bufferCount)
    {
        queue.InitMMap(bufferCount);
        foreach (var buffer in queue.BuffersPool.Buffers)
        {
            buffer.MapToMemory();
        }
    }

    /// <summary>
    /// Setup DMABUF capture queue with DRM PRIME buffers
    /// </summary>
    protected void SetupDmaBufCaptureQueue()
    {
        Logger.LogInformation("Setting up DMABUF capture queue with DRM PRIME buffers");
        var negotiatedFormat = Device.GetCaptureFormatMPlane();

        if (negotiatedFormat.NumPlanes != 1)
        {
            throw new Exception("We support only 1 plane");
        }

        DrmBuffers = DrmBufferManager!.AllocateFromFormat(
            negotiatedFormat.Width,
            negotiatedFormat.Height,
            negotiatedFormat.PlaneFormats[0],
            Configuration.CaptureBufferCount,
            new PixelFormat(negotiatedFormat.PixelFormat));

        if (DrmBuffers.Count != Configuration.CaptureBufferCount)
        {
            throw new Exception($"Failed to allocate {Configuration.CaptureBufferCount} DRM buffers");
        }

        var fds = DrmBuffers.Select(drmBuffer => drmBuffer.DmaBuffer.Fd).ToArray();
        Device.CaptureMPlaneQueue.InitDmaBuf(fds, negotiatedFormat.PlaneFormats[0].SizeImage, 0u);
        foreach (var buffer in DrmBuffers)
        {
            buffer.V4L2Buffer = Device
                .CaptureMPlaneQueue
                .DmaBufBuffersPool
                .Buffers
                .Single(b => b.DmaBufferFd == buffer.DmaBuffer.Fd);
        }
    }

    /// <summary>
    /// Start V4L2 streaming on both queues and start capture buffer processing thread
    /// </summary>
    protected virtual void StartStreaming()
    {
        Logger.LogInformation("Starting V4L2 streaming...");

        if (Configuration.UseDrmPrimeBuffers)
        {
            Device.CaptureMPlaneQueue.EnqueueAllDmaBufBuffers();
        }
        else
        {
            Device.CaptureMPlaneQueue.EnqueueAllBuffers();
        }

        Device.OutputMPlaneQueue.StreamOn();
        Device.CaptureMPlaneQueue.StreamOn();

        CaptureThreadCts = new CancellationTokenSource();
        CaptureThread = new Thread(ProcessCaptureBuffersThreadProc)
        {
            Name = "CaptureBufferProcessor",
            IsBackground = true
        };
        CaptureThread.Start();
        Logger.LogInformation("Started capture buffer processing thread");
    }

    /// <summary>
    /// Thread procedure for processing capture buffers using poll
    /// </summary>
    protected virtual void ProcessCaptureBuffersThreadProc()
    {
        var cancellationToken = CaptureThreadCts!.Token;
        Logger.LogInformation("Capture buffer processing thread started");

        while (!cancellationToken.IsCancellationRequested)
        {
            var dequeuedBuffer = Device.CaptureMPlaneQueue.WaitForReadyBuffer(1000);
            if (dequeuedBuffer == null)
            {
                continue;
            }

            FramesDecoded++;

            if (Configuration.UseDrmPrimeBuffers)
            {
                // For DMABUF mode, pass buffer index to caller
                ProcessDecodedBufferIndex!(DrmBuffers![(int)dequeuedBuffer.Index]);
                // Don't requeue - let the display system handle it
            }
            else
            {
                // For MMAP mode, copy data and requeue immediately
                var buffer = Device.CaptureMPlaneQueue.BuffersPool.Buffers[(int)dequeuedBuffer.Index];
                ProcessDecodedAction!(buffer.MappedPlanes[0].AsSpan());
                Device.CaptureMPlaneQueue.ReuseBuffer(dequeuedBuffer.Index);
            }
        }

        Logger.LogInformation("Capture buffer processing thread stopped");
    }

    /// <summary>
    /// Requeues a DMABUF capture buffer after display is done with it
    /// </summary>
    public void RequeueCaptureBuffer(SharedDmaBuffer buffer)
    {
        if (!Configuration.UseDrmPrimeBuffers)
        {
            throw new InvalidOperationException("RequeueCaptureBuffer can only be used with DRM PRIME buffers");
        }

        Device.CaptureMPlaneQueue.ReuseDmaBufBuffer(buffer.V4L2Buffer);
    }

    /// <summary>
    /// Cleanup decoder resources
    /// </summary>
    protected virtual void Cleanup()
    {
        Logger.LogInformation("Cleaning up decoder resources...");

        if (CaptureThreadCts != null)
        {
            CaptureThreadCts.Cancel();
            if (CaptureThread is { IsAlive: true })
            {
                if (!CaptureThread.Join(TimeSpan.FromSeconds(2)))
                {
                    Logger.LogWarning("Capture thread did not stop gracefully");
                }
            }

            CaptureThreadCts.Dispose();
            CaptureThreadCts = null;
            CaptureThread = null;
            Logger.LogInformation("Stopped capture buffer processing thread");
        }

        Device.OutputMPlaneQueue.StreamOff();
        Device.CaptureMPlaneQueue.StreamOff();

        UnmapBuffers(Device.OutputMPlaneQueue);

        if (!Configuration.UseDrmPrimeBuffers)
        {
            UnmapBuffers(Device.CaptureMPlaneQueue);
        }

        Logger.LogInformation("Decoder cleanup completed");
    }

    /// <summary>
    /// Unmap buffers in a queue
    /// </summary>
    protected void UnmapBuffers(V4L2DeviceQueue queue)
    {
        foreach (var buffer in queue.BuffersPool.Buffers)
        {
            buffer.Unmap();
        }
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (!Disposed)
        {
            Cleanup();
            Disposed = true;
        }

        GC.SuppressFinalize(this);
        await Task.CompletedTask;
    }
}
