using SharpVideo.Rtp;

namespace SharpVideo.Decoding.OhdDemo.ImguiOsd;

internal abstract class UiHostBase : IHostedService
{
    private readonly H264Depacketiser _h264Depacketiser = new();

    protected readonly CancellationTokenSource CancellationTokenSource = new();
    protected readonly ILoggerFactory LoggerFactory;
    protected readonly ILogger Logger;
    protected readonly BaseDecoder H264Decoder;

    protected Task? DrawThread;
    protected VideoFrameManager? VideoFrameManager;
    protected ImGuiUiRenderer? UiRenderer;

    protected abstract bool ShowDemoWindow { get; }

    protected UiHostBase(
        InMemoryPipeStreamAccessor h264Stream,
        DecodersFactory decodersFactory,
        ILoggerFactory loggerFactory,
        ILogger logger)
    {
        LoggerFactory = loggerFactory;
        Logger = logger;

        H264Decoder = decodersFactory.CreateH264Decoder();
        H264Decoder.Start();

        h264Stream.SetReceiveAction(ReceiveH624);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Starting {HostType}", GetType().Name);

        VideoFrameManager = new VideoFrameManager(
            H264Decoder,
            LoggerFactory.CreateLogger<VideoFrameManager>());

        UiRenderer = new ImGuiUiRenderer(
            LoggerFactory.CreateLogger<ImGuiUiRenderer>(),
            customRenderCallback: null,
            showDemoWindow: ShowDemoWindow);

        DrawThread = Task.Factory.StartNew(RunDrawThread, TaskCreationOptions.LongRunning);
        VideoFrameManager.Start();

        OnStart();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Stopping {HostType}", GetType().Name);
        await CancellationTokenSource.CancelAsync();

        if (DrawThread != null)
        {
            await DrawThread;
        }

        if (VideoFrameManager != null)
        {
            await VideoFrameManager.StopAsync();
            VideoFrameManager.Dispose();
        }

        OnStop();
    }

    /// <summary>
    /// Called after StartAsync completes initialization. Override for additional setup.
    /// </summary>
    protected virtual void OnStart() { }

    /// <summary>
    /// Called after StopAsync completes cleanup. Override for additional cleanup.
    /// </summary>
    protected virtual void OnStop() { }

    /// <summary>
    /// Main drawing thread implementation. Must be implemented by derived classes.
    /// </summary>
    protected abstract void RunDrawThread();

    private void ReceiveH624(ReadOnlyMemory<byte> payload)
    {
        var packet = new RTPPacket(payload.Span);
        var hdr = packet.Header;
        var frame = _h264Depacketiser.ProcessRTPPayload(packet.Payload, hdr.SequenceNumber, hdr.Timestamp, hdr.MarkerBit, out var isKeyFrame);
        if (frame != null)
        {
            ProcessNalu(frame);
        }
    }

    private void ProcessNalu(MemoryStream frame)
    {
        var buffer = H264Decoder.GetEncodedBuffersForReuse();
        if (buffer == null)
        {
            Logger.LogWarning("Skipping frame");
            return;
        }

        if (buffer is ManagedMemoryEncodedBuffer memBuf)
        {
            // Use the internal buffer of MemoryStream to avoid ToArray() copy
            var internalBuffer = frame.GetBuffer();
            memBuf.CopyFromSpan(internalBuffer.AsSpan(0, (int)frame.Length));
        }

        H264Decoder.AddBufferForDecode(buffer);
    }
}
