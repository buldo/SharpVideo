using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.H264;
using SharpVideo.Linux.Native;
using SharpVideo.V4L2;
using SharpVideo.V4L2DecodeDemo.Models;

namespace SharpVideo.V4L2DecodeDemo.Services;

[SupportedOSPlatform("linux")]
public class H264V4L2StatelessDecoder
{
    private readonly V4L2Device _device;
    private readonly V4L2DeviceQueue _deviceCaptureQueue;
    private readonly V4L2DeviceQueue _deviceOutputQueue;
    private readonly MediaDevice? _mediaDevice;
    private readonly ILogger<H264V4L2StatelessDecoder> _logger;

    private readonly DecoderConfiguration _configuration;

    private readonly Queue<uint> _availableOutputBuffers = new();
    private readonly Queue<MediaRequest> _availableRequests = new();
    private readonly Dictionary<uint, MediaRequest> _inFlightRequests = new();
    private readonly object _bufferLock = new();

    private bool _disposed;
    private int _framesDecoded;
    private readonly Stopwatch _decodingStopwatch = new();

    private readonly bool _supportsSliceParamsControl;

    private int _consecutiveFailures = 0;

    // Thread for processing capture buffers
    private Thread? _captureThread;
    private CancellationTokenSource? _captureThreadCts;

    // DPB (Decoded Picture Buffer) tracking
    private readonly List<DpbEntry> _dpb = new();
    private uint _maxFrameNum = 256;

    private class DpbEntry
    {
        public uint FrameNum { get; set; }
        public uint PicOrderCnt { get; set; }
        public bool IsReference { get; set; }
        public bool IsLongTerm { get; set; }
    }

    public event EventHandler<FrameDecodedEventArgs>? FrameDecoded;

    public H264V4L2StatelessDecoder(
        V4L2Device device,
        MediaDevice? mediaDevice,
        ILogger<H264V4L2StatelessDecoder> logger,
        DecoderConfiguration? configuration = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _deviceCaptureQueue = device.GetQueue(V4L2BufferType.VIDEO_CAPTURE_MPLANE, V4L2Memory.MMAP);
        _deviceOutputQueue = device.GetQueue(V4L2BufferType.VIDEO_OUTPUT_MPLANE, V4L2Memory.MMAP);
        _mediaDevice = mediaDevice;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? new DecoderConfiguration();
        _supportsSliceParamsControl =
            device.ExtendedControls.Any(c => c.Id == V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SLICE_PARAMS);
    }

    /// <summary>
    /// Decodes H.264 stream using V4L2 hardware acceleration with efficient stream processing
    /// </summary>
    public async Task DecodeStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        if (!stream.CanRead)
            throw new ArgumentException("Stream must be readable", nameof(stream));

        _logger.LogInformation("Starting H.264 stateless stream decode");
        _decodingStopwatch.Start();

        InitializeDecoder();

        using var naluProvider = new H264AnnexBNaluProvider();
        _logger.LogInformation("NALU provider created; beginning stream processing");
        var naluProcessingTask = ProcessNalusAsync(naluProvider, cancellationToken);
        var feedTask = FeedStreamToNaluProviderAsync(stream, naluProvider, cancellationToken);
        await Task.WhenAll(naluProcessingTask, feedTask);

        // Drain the pipeline: wait for all queued frames to be processed by hardware
        _logger.LogInformation("Draining decoder pipeline...");
        int drainAttempts = 0;
        int lastFrameCount = _framesDecoded;
        while (drainAttempts < 200) // Up to 2 seconds of draining (fast hardware)
        {
            ReclaimOutputBuffers();

            // Check if we have frames in the pipeline
            lock (_bufferLock)
            {
                int buffersInUse = _deviceOutputQueue.Buffers.Count - _availableOutputBuffers.Count;
                if (buffersInUse == 0)
                {
                    _logger.LogInformation("Pipeline drained, all buffers returned");
                    break;
                }

                _logger.LogDebug("Still draining: {BuffersInUse} buffers in use", buffersInUse);
            }

            // Check for progress
            if (_framesDecoded != lastFrameCount)
            {
                _logger.LogDebug("Decoded {NewFrames} more frames during drain", _framesDecoded - lastFrameCount);
                lastFrameCount = _framesDecoded;
                drainAttempts = 0; // Reset timeout if we're making progress
            }

            Thread.Sleep(10); // Wait 10ms between drain attempts (fast for high-performance hardware)
            drainAttempts++;
        }

        _decodingStopwatch.Stop();
        var elapsedSeconds = _decodingStopwatch.Elapsed.TotalSeconds;
        var fps = elapsedSeconds > 0 ? _framesDecoded / elapsedSeconds : 0;

        _logger.LogInformation(
            "Stateless decoding completed successfully. {FrameCount} frames in {ElapsedTime:F2}s ({FPS:F2} fps)",
            _framesDecoded,
            elapsedSeconds,
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
        const int bufferSize = 64 * 1024; // 64KB buffer
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

                if (readIterations <= 5 || readIterations % 100 == 0)
                {
                    _logger.LogInformation(
                        "Read chunk #{Chunk} ({Bytes} bytes); total fed {Total} bytes",
                        readIterations,
                        bytesRead,
                        totalBytesRead);
                }
            }
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
        var parsingOptions = new ParsingOptions();
        var naluCount = 0;
        await foreach (var naluData in naluProvider.NaluReader.ReadAllAsync(cancellationToken))
        {
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

            ProcessNaluByType(naluData, naluState, streamState); // Use WithoutHeader instead of manual span slicing
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

        // Reclaim output buffers that have been processed
        for (int i = 0; i < 3; i++)
        {
            ReclaimOutputBuffers();
        }

        SubmitFrameToDevice(nalu.Data, header, isKeyFrame, streamState);
    }

    private void SubmitFrameToDevice(
        ReadOnlySpan<byte> frameData,
        SliceHeaderState header,
        bool isKeyFrame,
        H264BitstreamParserState streamState)
    {
        var (bufferIndex, mappedBuffer) = AcquireOutputBuffer();

        if (frameData.Length > mappedBuffer.MappedPlanes[0].Length)
        {
            throw new Exception("Output buffer too small");
        }

        mappedBuffer.CopyDataToPlane(frameData, 0);

        MediaRequest? request = null;
        if (_mediaDevice != null)
        {
            request = AcquireMediaRequest();
            SubmitFrameControls(header, isKeyFrame, request, streamState);
        }
        _deviceOutputQueue.Enqueue(mappedBuffer, request);

        if (request != null)
        {
            request.Queue();

            lock (_bufferLock)
            {
                _inFlightRequests[bufferIndex] = request;
            }
        }
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
            var sliceParams = BuildSliceParams(header);
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

    private static V4L2CtrlH264SliceParams BuildSliceParams(SliceHeaderState header)
    {
        var sliceParams = new V4L2CtrlH264SliceParams
        {
            HeaderBitSize = 0,
            FirstMbInSlice = header.first_mb_in_slice,
            SliceType = (byte)(header.slice_type & 0x1F),
            ColourPlaneId = (byte)(header.colour_plane_id & 0x3),
            RedundantPicCnt = (byte)Math.Min(header.redundant_pic_cnt, byte.MaxValue),
            CabacInitIdc = (byte)Math.Min(header.cabac_init_idc, byte.MaxValue),
            SliceQpDelta = ClampToSByte(header.slice_qp_delta),
            SliceQsDelta = ClampToSByte(header.slice_qs_delta),
            DisableDeblockingFilterIdc = (byte)Math.Min(header.disable_deblocking_filter_idc, byte.MaxValue),
            SliceAlphaC0OffsetDiv2 = ClampToSByte(header.slice_alpha_c0_offset_div2),
            SliceBetaOffsetDiv2 = ClampToSByte(header.slice_beta_offset_div2),
            NumRefIdxL0ActiveMinus1 = (byte)Math.Min(header.num_ref_idx_l0_active_minus1, byte.MaxValue),
            NumRefIdxL1ActiveMinus1 = (byte)Math.Min(header.num_ref_idx_l1_active_minus1, byte.MaxValue),
            Reserved = 0,
            RefPicList0 = CreateReferenceList(),
            RefPicList1 = CreateReferenceList(),
            Flags = 0
        };

        return sliceParams;
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
        for (int i = 0; i < _dpb.Count && i < dpbArray.Length; i++)
        {
            dpbArray[i].FrameNum = (ushort)_dpb[i].FrameNum;
            dpbArray[i].PicNum = (ushort)_dpb[i].FrameNum;
            dpbArray[i].TopFieldOrderCnt = (int)_dpb[i].PicOrderCnt;
            dpbArray[i].BottomFieldOrderCnt = (int)_dpb[i].PicOrderCnt;
            dpbArray[i].Flags = V4L2H264Constants.V4L2_H264_DPB_ENTRY_FLAG_VALID;

            if (_dpb[i].IsReference)
            {
                dpbArray[i].Flags |= V4L2H264Constants.V4L2_H264_DPB_ENTRY_FLAG_ACTIVE;
            }

            if (_dpb[i].IsLongTerm)
            {
                dpbArray[i].Flags |= V4L2H264Constants.V4L2_H264_DPB_ENTRY_FLAG_LONG_TERM;
            }
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
            _dpb.Add(newEntry);
            _logger.LogTrace("Added reference frame to DPB: frame_num={FrameNum}, DPB size={Size}", header.frame_num,
                _dpb.Count);
        }

        // Manage DPB size - remove oldest frames if we exceed max size
        var maxDpbSize = sps.sps_data.max_num_ref_frames;

        while (_dpb.Count > maxDpbSize)
        {
            _dpb.RemoveAt(0); // Remove oldest
            _logger.LogTrace("Removed oldest DPB entry, new size={Size}", _dpb.Count);
        }

        return decodeParams;
    }

    private static V4L2H264Reference[] CreateReferenceList()
    {
        return new V4L2H264Reference[V4L2H264Constants.V4L2_H264_REF_LIST_LEN];
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

    private static sbyte ClampToSByte(int value)
    {
        if (value < sbyte.MinValue)
            return sbyte.MinValue;
        if (value > sbyte.MaxValue)
            return sbyte.MaxValue;
        return (sbyte)value;
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
            // Wait for capture buffers to be ready (1 second timeout)
            var (pollResult, revents) = _deviceCaptureQueue.Poll(1000);

                if (pollResult < 0)
                {
                    var errno = Marshal.GetLastPInvokeError();
                    if (errno == 4) // EINTR - interrupted by signal
                    {
                        continue;
                    }
                    _logger.LogError("Poll failed with errno {Errno}", errno);
                    break;
                }

                if (pollResult == 0)
                {
                    // Timeout - check cancellation and continue
                    continue;
                }

                // Check if there's data ready - if not, skip processing
                if (!revents.HasFlag(PollEvents.POLLIN))
                {
                    continue;
                }

                // Process all available capture buffers
                while (!cancellationToken.IsCancellationRequested)
                {
                    var dequeuedBuffer = _deviceCaptureQueue.Dequeue();
                    if (dequeuedBuffer == null)
                    {
                        // No more buffers available
                        break;
                    }

                    _framesDecoded++;
                    FrameDecoded?.Invoke(this, new FrameDecodedEventArgs
                    {
                        FrameNumber = _framesDecoded,
                        BytesUsed = dequeuedBuffer.TotalBytesUsed,
                        Timestamp = DateTime.UtcNow
                    });

                    var mappedBuffer = _deviceCaptureQueue.Buffers[(int)dequeuedBuffer.Index];

                    // Reset plane bytes used before requeueing
                    for (int p = 0; p < mappedBuffer.Planes.Length; p++)
                    {
                        mappedBuffer.Planes[p].BytesUsed = 0;
                    }

                    try
                    {
                        _deviceCaptureQueue.Enqueue(mappedBuffer);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to requeue capture buffer {Index}", dequeuedBuffer.Index);
                    }
                }
            }

        _logger.LogInformation("Capture buffer processing thread stopped");
    }

    private void ReclaimOutputBuffers(int timeoutMs = 0)
    {
        while (true)
        {
            var dequeuedBuffer = _deviceOutputQueue.Dequeue();
            if (dequeuedBuffer == null)
            {
                // No more buffers available
                break;
            }

            ReturnOutputBuffer(dequeuedBuffer.Index);

            if (_mediaDevice != null)
            {
                MediaRequest? request = null;
                lock (_bufferLock)
                {
                    if (_inFlightRequests.TryGetValue(dequeuedBuffer.Index, out var existing))
                    {
                        request = existing;
                        _inFlightRequests.Remove(dequeuedBuffer.Index);
                    }
                }

                if (request != null)
                {
                    ReleaseMediaRequest(request);
                }
            }
        }
    }

    private (uint bufferIndex, V4L2MMapMPlaneBuffer mappedBuffer) AcquireOutputBuffer()
    {
        const int maxRetries = 10;
        const int pollTimeoutMs = 500; // 500ms timeout for each poll attempt

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            // First try to reclaim any processed buffers without blocking
            ReclaimOutputBuffers(timeoutMs: 0);

            lock (_bufferLock)
            {
                if (_availableOutputBuffers.Count > 0)
                {
                    var bufferIndex = _availableOutputBuffers.Dequeue();
                    if (attempt > 0)
                    {
                        _logger.LogDebug("Acquired output buffer after {Attempts} attempts", attempt + 1);
                    }
                    return (bufferIndex, _deviceOutputQueue.Buffers[(int)bufferIndex]);
                }
            }

            // If no buffers available, log and wait for buffers to be returned by hardware
            if (attempt == 0)
            {
                int buffersInUse;
                lock (_bufferLock)
                {
                    buffersInUse = _deviceOutputQueue.Buffers.Count - _availableOutputBuffers.Count;
                }
                _logger.LogDebug(
                    "No output buffers immediately available ({InUse}/{Total} in use), polling for available buffers...",
                    buffersInUse,
                    _deviceOutputQueue.Buffers.Count);
            }

            // Poll with timeout to wait for output buffers to become available
            ReclaimOutputBuffers(timeoutMs: pollTimeoutMs);
        }

        // All retries exhausted
        int finalBuffersInUse;
        lock (_bufferLock)
        {
            finalBuffersInUse = _deviceOutputQueue.Buffers.Count - _availableOutputBuffers.Count;
        }

        throw new Exception(
            $"No available output buffers after {maxRetries} attempts (timeout: {maxRetries * pollTimeoutMs}ms). " +
            $"Buffers in use: {finalBuffersInUse}/{_deviceOutputQueue.Buffers.Count}. " +
            $"Hardware may be stalled or not processing buffers.");
    }

    private void ReturnOutputBuffer(uint index)
    {
        lock (_bufferLock)
        {
            _availableOutputBuffers.Enqueue(index);
        }
    }

    private void InitializeDecoder()
    {
        _logger.LogInformation("Initializing H.264 stateless decoder...");

        // Log device information for debugging
        _logger.LogInformation("Device fd: {Fd}, Controls: {ControlCount}, ExtControls: {ExtControlCount}",
            _device.fd, _device.Controls.Count, _device.ExtendedControls.Count);

        // Configure decoder formats
        ConfigureFormats();

        InitializeMediaRequests();

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
        if (!VerifyStreamingState())
        {
            throw new InvalidOperationException("Failed to verify streaming state after initialization");
        }

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
        var confirmedCaptureFormat = _device.GetCaptureFormatMPlane();

        _logger.LogInformation(
            "Set capture format: {Width}x{Height} fmt=0x{Pixel:X8}",
            confirmedCaptureFormat.Width,
            confirmedCaptureFormat.Height,
            confirmedCaptureFormat.PixelFormat);
    }

    private void SetupAndMapBuffers()
    {
        _logger.LogInformation("Setting up and mapping buffers...");

        // Setup OUTPUT buffers for slice data with proper V4L2 mmap
        SetupBufferQueue(
            V4L2BufferType.VIDEO_OUTPUT_MPLANE,
            _configuration.OutputBufferCount,
            _availableOutputBuffers);

        // Setup CAPTURE buffers for decoded frames with proper V4L2 mmap
        SetupBufferQueue(V4L2BufferType.VIDEO_CAPTURE_MPLANE, _configuration.CaptureBufferCount,
            null);
    }

    private void InitializeMediaRequests()
    {
        if (_mediaDevice == null)
        {
            return;
        }

        _mediaDevice.AllocateMediaRequests(_configuration.RequestPoolSize);
        foreach (var request in _mediaDevice.OpenedRequests)
        {
            lock (_bufferLock)
            {
                _availableRequests.Enqueue(request);
            }
        }
    }

    private MediaRequest AcquireMediaRequest()
    {
        if (_mediaDevice == null)
        {
            throw new Exception("Media device not available");
        }

        lock (_bufferLock)
        {
            if (_availableRequests.Count > 0)
            {
                return _availableRequests.Dequeue();
            }
            else
            {
                throw new Exception("No available media requests");
            }
        }
    }

    private void ReleaseMediaRequest(MediaRequest request)
    {
        request.ReInit();
        lock (_bufferLock)
        {
            _availableRequests.Enqueue(request);
        }
    }

    private void SetupBufferQueue(
        V4L2BufferType bufferType,
        uint bufferCount,
        Queue<uint>? availableQueue)
    {
        var queue = _device.GetQueue(bufferType, V4L2Memory.MMAP);
        queue.RequestBuffers(bufferCount);
        var mappedBuffers = queue.Buffers;

        if (availableQueue != null)
        {
            for (int i = 0; i < mappedBuffers.Count; i++)
            {
                availableQueue.Enqueue((uint)i);
            }
        }
    }

    private void StartStreaming()
    {
        _logger.LogInformation("Starting V4L2 streaming...");

        try
        {
            // Queue all capture buffers before starting streaming
            for (uint i = 0; i < _deviceCaptureQueue.Buffers.Count; i++)
            {
                var mappedBuffer = _deviceCaptureQueue.Buffers[(int)i];
                _deviceCaptureQueue.Enqueue(mappedBuffer);
            }

            // Start streaming on OUTPUT queue first
            _device.StreamOn(V4L2BufferType.VIDEO_OUTPUT_MPLANE);
            _logger.LogTrace("Started OUTPUT streaming");

            // Start streaming on CAPTURE queue
            _device.StreamOn(V4L2BufferType.VIDEO_CAPTURE_MPLANE);
            _logger.LogTrace("Started CAPTURE streaming");

            _logger.LogInformation("V4L2 streaming started successfully");

            // Start capture buffer processing thread
            _captureThreadCts = new CancellationTokenSource();
            _captureThread = new Thread(ProcessCaptureBuffersThreadProc)
            {
                Name = "CaptureBufferProcessor",
                IsBackground = true
            };
            _captureThread.Start();
            _logger.LogInformation("Started capture buffer processing thread");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start streaming");
            throw new InvalidOperationException($"Failed to start streaming: {ex.Message}", ex);
        }
    }

    private bool VerifyStreamingState()
    {
        var outputFormat = _device.GetOutputFormatMPlane();
        var captureFormat = _device.GetCaptureFormatMPlane();

        _logger.LogDebug("Streaming verification: Output {OutputFormat:X8}, Capture {CaptureFormat:X8}",
            outputFormat.PixelFormat, captureFormat.PixelFormat);

        return outputFormat.PixelFormat != 0 && captureFormat.PixelFormat != 0;
    }

    private void Cleanup()
    {
        _logger.LogInformation("Cleaning up decoder resources...");

        try
        {
            // Stop capture buffer processing thread
            if (_captureThreadCts != null)
            {
                _captureThreadCts.Cancel();
                if (_captureThread != null && _captureThread.IsAlive)
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

            // Stop streaming
            if (_device?.fd > 0)
            {
                _device.StreamOff(V4L2BufferType.VIDEO_OUTPUT_MPLANE);
                _device.StreamOff(V4L2BufferType.VIDEO_CAPTURE_MPLANE);
            }

            // Unmap buffers
            UnmapBuffers(_deviceOutputQueue);
            UnmapBuffers(_deviceCaptureQueue);

            // Clear buffer queues
            lock (_bufferLock)
            {
                _availableOutputBuffers.Clear();
            }

            _logger.LogInformation("Decoder cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during cleanup");
        }
    }

    private void UnmapBuffers(V4L2DeviceQueue queue)
    {
        foreach (var buffer in queue.Buffers)
        {
            for (int p = 0; p < buffer.MappedPlanes.Count; p++)
            {
                var planePtr = buffer.MappedPlanes[p].Pointer;
                if (planePtr == IntPtr.Zero)
                    continue;

                try
                {
                    unsafe
                    {
                        var result = Libc.munmap((void*)planePtr, buffer.MappedPlanes[p].Length);
                        if (result != 0)
                        {
                            var errno = Marshal.GetLastPInvokeError();
                            _logger.LogWarning("munmap failed for buffer {Index} plane {Plane}: errno {Errno}",
                                buffer.Index, p, errno);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to munmap buffer {Index} plane {Plane}", buffer.Index, p);
                }
            }
        }
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