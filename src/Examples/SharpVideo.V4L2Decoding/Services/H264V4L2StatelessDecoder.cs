using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.H264;
using SharpVideo.Linux.Native;
using SharpVideo.Utils;
using SharpVideo.V4L2;
using SharpVideo.V4L2Decoding.Models;

namespace SharpVideo.V4L2Decoding.Services;

[SupportedOSPlatform("linux")]
public class H264V4L2StatelessDecoder
{
    private readonly V4L2Device _device;
    private readonly MediaDevice? _mediaDevice;
    private readonly ILogger<H264V4L2StatelessDecoder> _logger;

    private readonly DecoderConfiguration _configuration;
    private readonly Action<ReadOnlySpan<byte>>? _processDecodedAction;
    private Action<SharedDmaBuffer>? _processDecodedBufferIndex;
    private readonly DrmBufferManager _drmBufferManager;
    private List<SharedDmaBuffer>? _drmBuffers;

    private bool _disposed;
    private int _framesDecoded;

    private readonly bool _supportsSliceParamsControl;

    // Thread for processing capture buffers
    private Thread? _captureThread;
    private CancellationTokenSource? _captureThreadCts;

    // DPB (Decoded Picture Buffer) tracking - using Queue for O(1) operations
    private readonly Queue<DpbEntry> _dpb = new();

    public H264V4L2StatelessDecoder(
        V4L2Device device,
        MediaDevice? mediaDevice,
        ILogger<H264V4L2StatelessDecoder> logger,
        DecoderConfiguration configuration,
        Action<ReadOnlySpan<byte>>? processDecodedAction,
        DrmBufferManager drmBufferManager)
    {
        _device = device;
        _mediaDevice = mediaDevice;
        _logger = logger;
        _configuration = configuration;
        _processDecodedAction = processDecodedAction;
        _drmBufferManager = drmBufferManager;
        _supportsSliceParamsControl =
            device.ExtendedControls.Any(c => c.Id == V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SLICE_PARAMS);

        if (_configuration.UseDrmPrimeBuffers && _drmBufferManager == null)
        {
            throw new ArgumentException("DrmBufferManager is required when UseDrmPrimeBuffers is true");
        }
    }

    public H264V4L2StatelessDecoderStatistics Statistics { get; } = new();

    /// <summary>
    /// Decodes H.264 stream using V4L2 hardware acceleration with efficient stream processing
    /// </summary>
    public async Task DecodeStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (!stream.CanRead)
        {
            throw new ArgumentException("Stream must be readable", nameof(stream));
        }

        _logger.LogInformation("Starting H.264 stateless stream decode");
        var decodingStopwatch = Stopwatch.StartNew();

        using var naluProvider = new H264AnnexBNaluProvider();
        _logger.LogInformation("NALU provider created; beginning stream processing");
        var naluProcessingTask = ProcessNalusAsync(naluProvider, cancellationToken);
        var feedTask = FeedStreamToNaluProviderAsync(stream, naluProvider, cancellationToken);
        await Task.WhenAll(naluProcessingTask, feedTask);

        // Drain the pipeline: wait for all queued frames to be processed by hardware
        _logger.LogInformation("Draining decoder pipeline...");
        int drainAttempts = 0;
        int lastFrameCount = _framesDecoded;

        // Use more aggressive polling initially, then back off
        while (drainAttempts < 100) // Reduced from 2000 to 100 iterations
        {
            _device.OutputMPlaneQueue.ReclaimProcessed();

            // Check for progress
            if (_framesDecoded != lastFrameCount)
            {
                _logger.LogDebug("Decoded {NewFrames} more frames during drain", _framesDecoded - lastFrameCount);
                lastFrameCount = _framesDecoded;
                drainAttempts = 0; // Reset timeout if we're making progress
            }

            // Short sleep instead of SpinWait for better CPU usage
            Thread.Sleep(1);
            drainAttempts++;
        }

        decodingStopwatch.Stop();
        Statistics.DecodeElapsed = decodingStopwatch.Elapsed;
        var fps = Statistics.DecodeElapsed.TotalSeconds > 0
            ? _framesDecoded / Statistics.DecodeElapsed.TotalSeconds
            : 0;

        _logger.LogInformation(
            "Stateless decoding completed successfully. {FrameCount} frames in {ElapsedTime:F2}s ({FPS:F2} fps)",
            _framesDecoded,
            Statistics.DecodeElapsed.TotalSeconds,
            fps);
    }

    /// <summary>
    /// Feeds stream data to NaluProvider in chunks
    /// </summary>
    private async Task FeedStreamToNaluProviderAsync(
        Stream stream,
        H264AnnexBNaluProvider naluProvider,
        CancellationToken cancellationToken)
    {
        const int bufferSize = 16 * 1024; // Reduced from 64KB to 16KB for lower latency
        var buffer = new byte[bufferSize];
        long totalBytesRead = 0;

        try
        {
            int readIterations = 0;
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await naluProvider.AppendData(buffer.AsSpan(0, bytesRead).ToArray(), cancellationToken);
                totalBytesRead += bytesRead;
                readIterations++;

                if (readIterations <= 5 || readIterations % 500 == 0)
                {
                    _logger.LogInformation(
                        "Read chunk #{Chunk} ({Bytes} bytes); total fed {Total} bytes",
                        readIterations,
                        bytesRead,
                        totalBytesRead);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while feeding stream to NALU provider");
            throw;
        }
        finally
        {
            naluProvider.CompleteWriting();
            _logger.LogInformation("Completed feeding bitstream: {BytesRead} bytes", totalBytesRead);
        }
    }

    /// <summary>
    /// Processes NALUs asynchronously as they become available
    /// </summary>
    private async Task ProcessNalusAsync(H264AnnexBNaluProvider naluProvider, CancellationToken cancellationToken)
    {
        var streamState = new H264BitstreamParserState();
        var parsingOptions = new ParsingOptions
        {
            add_checksum = false // Disable checksum calculation for performance
        };
        var naluCount = 0;
        try
        {
            await foreach (var naluData in naluProvider.NaluReader.ReadAllAsync(cancellationToken))
            {
                _logger.LogTrace("Processing NALU #{Index} (size: {Size} bytes)", naluCount + 1, naluData.Data.Length);
                if (naluData.Data.Length < 1)
                {
                    continue;
                }

                var naluState = H264NalUnitParser.ParseNalUnit(naluData.WithoutHeader, streamState, parsingOptions);

                if (naluState == null)
                {
                    _logger.LogWarning("Parser returned null for NALU #{Index}; skipping", naluCount + 1);
                    continue;
                }

                naluCount++;

                ProcessNaluByType(naluData, naluState, streamState);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while ProcessNalusAsync");
            throw;
        }

        _logger.LogInformation("Finished processing NALUs ({Count} total)", naluCount);
    }

    /// <summary>
    /// Processes individual NALU based on its type
    /// </summary>
    private void ProcessNaluByType(H264Nalu naluData, NalUnitState naluState, H264BitstreamParserState streamState)
    {
        var naluType = (NalUnitType)naluState.nal_unit_header.nal_unit_type;

        switch (naluType)
        {
            case NalUnitType.SPS_NUT:
            case NalUnitType.PPS_NUT:
                _logger.LogTrace("{NaluType} was found in stream", naluType);
                break;

            case NalUnitType.CODED_SLICE_OF_NON_IDR_PICTURE_NUT: // Non-IDR slice
            case NalUnitType.CODED_SLICE_OF_IDR_PICTURE_NUT: // IDR slice
                _logger.LogTrace("Processing slice NALU type {NaluType}", naluType);
                HandleSliceNalu(naluData, naluState.nal_unit_payload.slice_layer_without_partitioning_rbsp, naluType,
                    streamState);
                break;

            default:
                _logger.LogTrace("Skipping NALU type {NaluType}", naluType);
                break;
        }
    }

    /// <summary>
    /// Handles slice NALUs (actual video data)
    /// </summary>
    private void HandleSliceNalu(
        H264Nalu nalu,
        SliceLayerWithoutPartitioningRbspState sliceLayerWithoutPartitioningRbsp,
        NalUnitType naluType,
        H264BitstreamParserState streamState)
    {
        var header = sliceLayerWithoutPartitioningRbsp.slice_header;
        if (header.first_mb_in_slice != 0)
        {
            _logger.LogDebug("Skipping non-initial slice for frame {FrameNum} in frame-based mode", header.frame_num);
            return;
        }

        var isKeyFrame = naluType == NalUnitType.CODED_SLICE_OF_IDR_PICTURE_NUT;

        SubmitFrameToDevice(nalu.Data, header, isKeyFrame, streamState);
    }

    private void SubmitFrameToDevice(
        ReadOnlySpan<byte> frameData,
        SliceHeaderState header,
        bool isKeyFrame,
        H264BitstreamParserState streamState)
    {
        // First, ensure there's a free buffer available before acquiring media request
        _device.OutputMPlaneQueue.EnsureFreeBuffer();

        // Now acquire media request if needed (buffer is guaranteed to be available)
        MediaRequest? request = null;
        if (_mediaDevice != null)
        {
            request = _device.OutputMPlaneQueue.AcquireMediaRequest();
            SubmitFrameControls(header, isKeyFrame, request, streamState);
        }

        // Write buffer and enqueue
        _device.OutputMPlaneQueue.WriteBufferAndEnqueue(frameData, request);
        request?.Queue();
    }

    private void SubmitFrameControls(
        SliceHeaderState header,
        bool isKeyFrame,
        MediaRequest request,
        H264BitstreamParserState streamState)
    {
        var pps = streamState.pps[header.pic_parameter_set_id];
        var ppsV4L2 = PpsMapper.ConvertPpsStateToV4L2(pps);
        _device.SetSingleExtendedControl(
            V4l2ControlsConstants.V4L2_CID_STATELESS_H264_PPS,
            ppsV4L2,
            request);

        var sps = streamState.sps[pps.seq_parameter_set_id];
        var spsV4L2 = SpsMapper.MapSpsToV4L2(sps);
        _device.SetSingleExtendedControl(
            V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SPS,
            spsV4L2,
            request);

        if (_supportsSliceParamsControl)
        {
            var sliceParams = SliceParamsMapper.BuildSliceParams(header);
            _device.SetSingleExtendedControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SLICE_PARAMS,
                sliceParams,
                request);
        }

        var decodeParams = BuildDecodeParams(header, isKeyFrame, sps);
        _device.SetSingleExtendedControl(
            V4l2ControlsConstants.V4L2_CID_STATELESS_H264_DECODE_PARAMS,
            decodeParams,
            request);
    }

    private V4L2CtrlH264DecodeParams BuildDecodeParams(SliceHeaderState header, bool isIdr, SpsState sps)
    {
        // Handle IDR frames - they reset the DPB
        if (isIdr)
        {
            _dpb.Clear();
            _logger.LogDebug("IDR frame detected - DPB cleared");
        }

        var dpbArray = CreateEmptyDpb();

        // Populate DPB with current reference frames
        int dpbIndex = 0;
        foreach (var entry in _dpb)
        {
            if (dpbIndex >= dpbArray.Length)
                break;

            dpbArray[dpbIndex].FrameNum = (ushort)entry.FrameNum;
            dpbArray[dpbIndex].PicNum = (ushort)entry.FrameNum;
            dpbArray[dpbIndex].TopFieldOrderCnt = (int)entry.PicOrderCnt;
            dpbArray[dpbIndex].BottomFieldOrderCnt = (int)entry.PicOrderCnt;
            dpbArray[dpbIndex].Flags = V4L2H264Constants.V4L2_H264_DPB_ENTRY_FLAG_VALID;

            if (entry.IsReference)
            {
                dpbArray[dpbIndex].Flags |= V4L2H264Constants.V4L2_H264_DPB_ENTRY_FLAG_ACTIVE;
            }

            if (entry.IsLongTerm)
            {
                dpbArray[dpbIndex].Flags |= V4L2H264Constants.V4L2_H264_DPB_ENTRY_FLAG_LONG_TERM;
            }

            dpbIndex++;
        }

        var decodeParams = new V4L2CtrlH264DecodeParams
        {
            Dpb = dpbArray,
            NalRefIdc = (ushort)Math.Min(header.nal_ref_idc, ushort.MaxValue),
            FrameNum = (ushort)Math.Min(header.frame_num, ushort.MaxValue),
            TopFieldOrderCnt = (int)header.pic_order_cnt_lsb,
            BottomFieldOrderCnt = (int)header.pic_order_cnt_lsb,
            IdrPicId = (ushort)Math.Min(header.idr_pic_id, ushort.MaxValue),
            PicOrderCntLsb = (ushort)Math.Min(header.pic_order_cnt_lsb, ushort.MaxValue),
            DeltaPicOrderCntBottom = header.delta_pic_order_cnt_bottom,
            DeltaPicOrderCnt0 = header.delta_pic_order_cnt.Count > 0 ? header.delta_pic_order_cnt[0] : 0,
            DeltaPicOrderCnt1 = header.delta_pic_order_cnt.Count > 1 ? header.delta_pic_order_cnt[1] : 0,
            DecRefPicMarkingBitSize = 0,
            PicOrderCntBitSize = 0,
            SliceGroupChangeCycle = header.slice_group_change_cycle,
            Reserved = 0,
            Flags = DetermineDecodeFlags(header, isIdr)
        };

        // Add current frame to DPB if it's a reference frame
        if (header.nal_ref_idc > 0)
        {
            var newEntry = new DpbEntry
            {
                FrameNum = (uint)header.frame_num,
                PicOrderCnt = (uint)header.pic_order_cnt_lsb,
                IsReference = true,
                IsLongTerm = false
            };
            _dpb.Enqueue(newEntry);
            _logger.LogTrace("Added reference frame to DPB: frame_num={FrameNum}, DPB size={Size}", header.frame_num,
                _dpb.Count);
        }

        // Manage DPB size - remove oldest frames if we exceed max size (now O(1) with Queue)
        var maxDpbSize = sps.sps_data.max_num_ref_frames;

        while (_dpb.Count > maxDpbSize)
        {
            _dpb.Dequeue(); // O(1) operation
            _logger.LogTrace("Removed oldest DPB entry, new size={Size}", _dpb.Count);
        }

        return decodeParams;
    }

    private static V4L2H264DpbEntry[] CreateEmptyDpb()
    {
        var dpb = new V4L2H264DpbEntry[V4L2H264Constants.V4L2_H264_NUM_DPB_ENTRIES];
        for (int i = 0; i < dpb.Length; i++)
        {
            dpb[i] = new V4L2H264DpbEntry
            {
                Reserved = new byte[5]
            };
        }

        return dpb;
    }

    private static uint DetermineDecodeFlags(SliceHeaderState header, bool isIdr)
    {
        uint flags = 0;

        if (isIdr)
        {
            flags |= V4L2H264Constants.V4L2_H264_DECODE_PARAM_FLAG_IDR_PIC;
        }

        var sliceType = (uint)(header.slice_type % 5);
        switch (sliceType)
        {
            case 0: // P slice
            case 3: // SP slice
                flags |= V4L2H264Constants.V4L2_H264_DECODE_PARAM_FLAG_PFRAME;
                break;
            case 1: // B slice
                flags |= V4L2H264Constants.V4L2_H264_DECODE_PARAM_FLAG_BFRAME;
                break;
        }

        return flags;
    }

    /// <summary>
    /// Thread procedure for processing capture buffers using poll.
    /// </summary>
    private void ProcessCaptureBuffersThreadProc()
    {
        var cancellationToken = _captureThreadCts!.Token;
        _logger.LogInformation("Capture buffer processing thread started");

        while (!cancellationToken.IsCancellationRequested)
        {
            var dequeuedBuffer = _device.CaptureMPlaneQueue.WaitForReadyBuffer(1000);
            if (dequeuedBuffer == null)
            {
                continue;
            }

            _framesDecoded++;

            if (_configuration.UseDrmPrimeBuffers)
            {
                // For DMABUF mode, pass buffer index to caller
                _processDecodedBufferIndex(_drmBuffers[(int)dequeuedBuffer.Index]);
                // Don't requeue - let the display system handle it
            }
            else
            {
                // For MMAP mode, copy data and requeue immediately
                var buffer = _device.CaptureMPlaneQueue.BuffersPool.Buffers[(int)dequeuedBuffer.Index];
                _processDecodedAction!(buffer.MappedPlanes[0].AsSpan());
                _device.CaptureMPlaneQueue.ReuseBuffer(dequeuedBuffer.Index);
            }
        }

        _logger.LogInformation("Capture buffer processing thread stopped");
    }

    public void InitializeDecoder(Action<SharedDmaBuffer>? processDecodedBufferIndex)
    {
        _logger.LogInformation("Initializing H.264 stateless decoder...");

        _processDecodedBufferIndex = processDecodedBufferIndex;
        if (_configuration.UseDrmPrimeBuffers && _processDecodedBufferIndex == null)
        {
            throw new ArgumentException(
                "processDecodedBufferIndex callback is required when UseDrmPrimeBuffers is true");
        }

        // Log device information for debugging
        _logger.LogInformation("Device fd: {Fd}, Controls: {ControlCount}, ExtControls: {ExtControlCount}",
            _device.fd, _device.Controls.Count, _device.ExtendedControls.Count);

        // Configure decoder formats
        ConfigureFormats();

        // For RK3566 I can only set FRAME_BASED + ANNEX_B
        var decodeMode = V4L2StatelessH264DecodeMode.FRAME_BASED;
        if (!_device.TrySetSimpleControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_DECODE_MODE,
                (int)decodeMode))
        {
            throw new Exception($"Failed to set decode mode to {decodeMode}");
        }

        var startCode = V4L2StatelessH264StartCode.ANNEX_B;
        if (!_device.TrySetSimpleControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_START_CODE,
                (int)startCode))
        {
            throw new Exception($"Failed to set start code to {startCode}");
        }

        // Setup and map buffers properly with real V4L2 mmap
        SetupAndMapBuffers();

        // Start streaming on both queues
        StartStreaming();

        // Verify streaming is actually working
        var outputFormat = _device.GetOutputFormatMPlane();
        var captureFormat = _device.GetCaptureFormatMPlane();

        _logger.LogDebug("Streaming verification: Output {OutputFormat:X8}, Capture {CaptureFormat:X8}",
            outputFormat.PixelFormat, captureFormat.PixelFormat);

        _logger.LogInformation("Decoder initialization completed successfully");
    }

    private void ConfigureFormats()
    {
        _logger.LogInformation("Configuring stateless decoder formats...");

        var outputFormat = new V4L2PixFormatMplane
        {
            Width = _configuration.InitialWidth,
            Height = _configuration.InitialHeight,
            PixelFormat = V4L2PixelFormats.V4L2_PIX_FMT_H264_SLICE,
            NumPlanes = 1,
            Field = (uint)V4L2Field.NONE,
            Colorspace = 5, // V4L2_COLORSPACE_REC709
            YcbcrEncoding = 1, // V4L2_YCBCR_ENC_DEFAULT
            Quantization = 1, // V4L2_QUANTIZATION_DEFAULT
            XferFunc = 1 // V4L2_XFER_FUNC_DEFAULT
        };
        _device.SetOutputFormatMPlane(outputFormat);

        var confirmedOutputFormat = _device.GetOutputFormatMPlane();

        _logger.LogInformation(
            "Set output format: {Width}x{Height} H264 ({Planes} plane(s))",
            confirmedOutputFormat.Width,
            confirmedOutputFormat.Height,
            confirmedOutputFormat.NumPlanes);

        // Configure capture format (decoded output)
        var captureFormat = new V4L2PixFormatMplane
        {
            Width = _configuration.InitialWidth,
            Height = _configuration.InitialHeight,
            PixelFormat = _configuration.PreferredPixelFormat, // Usually NV12
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

        // Setup OUTPUT buffers for slice data with proper V4L2 mmap
        SetupMMapBufferQueue(_device.OutputMPlaneQueue, _configuration.OutputBufferCount);
        if (_mediaDevice != null)
        {
            _mediaDevice.AllocateMediaRequests(_configuration.RequestPoolSize);
            _device.OutputMPlaneQueue.AssociateMediaRequests(_mediaDevice.OpenedRequests);
        }

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
        var negotiatedFormat = _device.GetCaptureFormatMPlane();

        if (negotiatedFormat.NumPlanes != 1)
        {
            throw new Exception("We support only 1 plane");
        }

        _drmBuffers = _drmBufferManager.AllocateFromFormat(
            negotiatedFormat.Width,
            negotiatedFormat.Height,
            negotiatedFormat.PlaneFormats[0],
            _configuration.CaptureBufferCount, new PixelFormat(negotiatedFormat.PixelFormat));

        if (_drmBuffers.Count != _configuration.CaptureBufferCount)
        {
            throw new Exception($"Failed to allocate {_configuration.CaptureBufferCount} DRM buffers");
        }

        var fds = _drmBuffers.Select(drmBuffer => drmBuffer.DmaBuffer.Fd).ToArray();
        _device.CaptureMPlaneQueue.InitDmaBuf(fds, negotiatedFormat.PlaneFormats[0].SizeImage, 0u);
        foreach (var buffer in _drmBuffers)
        {
            buffer.V4L2Buffer = _device
                .CaptureMPlaneQueue
                .DmaBufBuffersPool
                .Buffers
                .Single(b => b.DmaBufferFd == buffer.DmaBuffer.Fd);
        }
    }

    private void StartStreaming()
    {
        _logger.LogInformation("Starting V4L2 streaming...");

        if (_configuration.UseDrmPrimeBuffers)
        {
            _device.CaptureMPlaneQueue.EnqueueAllDmaBufBuffers();
        }
        else
        {
            _device.CaptureMPlaneQueue.EnqueueAllBuffers();
        }

        _device.OutputMPlaneQueue.StreamOn();
        _device.CaptureMPlaneQueue.StreamOn();

        _captureThreadCts = new CancellationTokenSource();
        _captureThread = new Thread(ProcessCaptureBuffersThreadProc)
        {
            Name = "CaptureBufferProcessor",
            IsBackground = true
        };
        _captureThread.Start();
        _logger.LogInformation("Started capture buffer processing thread");
    }

    private void Cleanup()
    {
        _logger.LogInformation("Cleaning up decoder resources...");

        if (_captureThreadCts != null)
        {
            _captureThreadCts.Cancel();
            if (_captureThread is { IsAlive: true })
            {
                if (!_captureThread.Join(TimeSpan.FromSeconds(2)))
                {
                    _logger.LogWarning("Capture thread did not stop gracefully");
                }
            }

            _captureThreadCts.Dispose();
            _captureThreadCts = null;
            _captureThread = null;
            _logger.LogInformation("Stopped capture buffer processing thread");
        }

        _device.OutputMPlaneQueue.StreamOff();
        _device.CaptureMPlaneQueue.StreamOff();

        UnmapBuffers(_device.OutputMPlaneQueue);

        if (!_configuration.UseDrmPrimeBuffers)
        {
            UnmapBuffers(_device.CaptureMPlaneQueue);
        }

        _logger.LogInformation("Decoder cleanup completed");
    }

    private void UnmapBuffers(V4L2DeviceQueue queue)
    {
        foreach (var buffer in queue.BuffersPool.Buffers)
        {
            buffer.Unmap();
        }
    }

    /// <summary>
    /// Requeues a DMABUF capture buffer after display is done with it.
    /// </summary>
    public void RequeueCaptureBuffer(SharedDmaBuffer buffer)
    {
        if (!_configuration.UseDrmPrimeBuffers)
        {
            throw new InvalidOperationException("RequeueCaptureBuffer can only be used with DRM PRIME buffers");
        }

        _device.CaptureMPlaneQueue.ReuseDmaBufBuffer(buffer.V4L2Buffer);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            Cleanup();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
        await Task.CompletedTask;
    }
}