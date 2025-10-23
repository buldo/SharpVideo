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
public class H264V4L2StatelessDecoder : H264V4L2DecoderBase
{
    private readonly MediaDevice? _mediaDevice;

    private readonly bool _supportsSliceParamsControl;

    // DPB (Decoded Picture Buffer) tracking - using Queue for O(1) operations
    private readonly Queue<DpbEntry> _dpb = new();

    public H264V4L2StatelessDecoder(
        V4L2Device device,
        MediaDevice? mediaDevice,
        ILogger<H264V4L2StatelessDecoder> logger,
        DecoderConfiguration configuration,
        Action<ReadOnlySpan<byte>>? processDecodedAction,
        DrmBufferManager? drmBufferManager)
        : base(device, logger, configuration, processDecodedAction, drmBufferManager)
    {
        _mediaDevice = mediaDevice;
        _supportsSliceParamsControl =
            device.ExtendedControls.Any(c => c.Id == V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SLICE_PARAMS);
    }

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

        Logger.LogInformation("Starting H.264 stateless stream decode");
        var decodingStopwatch = Stopwatch.StartNew();

        using var naluProvider = new H264AnnexBNaluProvider();
        Logger.LogInformation("NALU provider created; beginning stream processing");
        var naluProcessingTask = ProcessNalusAsync(naluProvider, cancellationToken);
        var feedTask = FeedStreamToNaluProviderAsync(stream, naluProvider, cancellationToken);
        await Task.WhenAll(naluProcessingTask, feedTask);

        // Drain the pipeline: wait for all queued frames to be processed by hardware
        Logger.LogInformation("Draining decoder pipeline...");
        int drainAttempts = 0;
        int lastFrameCount = FramesDecoded;

        // Use more aggressive polling initially, then back off
        while (drainAttempts < 100) // Reduced from 2000 to 100 iterations
        {
            Device.OutputMPlaneQueue.ReclaimProcessed();

            // Check for progress
            if (FramesDecoded != lastFrameCount)
            {
                Logger.LogDebug("Decoded {NewFrames} more frames during drain", FramesDecoded - lastFrameCount);
                lastFrameCount = FramesDecoded;
                drainAttempts = 0; // Reset timeout if we're making progress
            }

            // Short sleep instead of SpinWait for better CPU usage
            Thread.Sleep(1);
            drainAttempts++;
        }

        decodingStopwatch.Stop();
        Statistics.DecodeElapsed = decodingStopwatch.Elapsed;
        var fps = Statistics.DecodeElapsed.TotalSeconds > 0
            ? FramesDecoded / Statistics.DecodeElapsed.TotalSeconds
            : 0;

        Logger.LogInformation(
            "Stateless decoding completed successfully. {FrameCount} frames in {ElapsedTime:F2}s ({FPS:F2} fps)",
            FramesDecoded,
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
                    Logger.LogInformation(
                        "Read chunk #{Chunk} ({Bytes} bytes); total fed {Total} bytes",
                        readIterations,
                        bytesRead,
                        totalBytesRead);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error while feeding stream to NALU provider");
            throw;
        }
        finally
        {
            naluProvider.CompleteWriting();
            Logger.LogInformation("Completed feeding bitstream: {BytesRead} bytes", totalBytesRead);
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
                Logger.LogTrace("Processing NALU #{Index} (size: {Size} bytes)", naluCount + 1, naluData.Data.Length);
                if (naluData.Data.Length < 1)
                {
                    continue;
                }

                var naluState = H264NalUnitParser.ParseNalUnit(naluData.WithoutHeader, streamState, parsingOptions);

                if (naluState == null)
                {
                    Logger.LogWarning("Parser returned null for NALU #{Index}; skipping", naluCount + 1);
                    continue;
                }

                naluCount++;

                ProcessNaluByType(naluData, naluState, streamState);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error while ProcessNalusAsync");
            throw;
        }

        Logger.LogInformation("Finished processing NALUs ({Count} total)", naluCount);
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
                Logger.LogTrace("{NaluType} was found in stream", naluType);
                break;

            case NalUnitType.CODED_SLICE_OF_NON_IDR_PICTURE_NUT: // Non-IDR slice
            case NalUnitType.CODED_SLICE_OF_IDR_PICTURE_NUT: // IDR slice
                Logger.LogTrace("Processing slice NALU type {NaluType}", naluType);
                HandleSliceNalu(naluData, naluState.nal_unit_payload.slice_layer_without_partitioning_rbsp, naluType,
                    streamState);
                break;

            default:
                Logger.LogTrace("Skipping NALU type {NaluType}", naluType);
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
            Logger.LogDebug("Skipping non-initial slice for frame {FrameNum} in frame-based mode", header.frame_num);
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
        Device.OutputMPlaneQueue.EnsureFreeBuffer();

        // Now acquire media request if needed (buffer is guaranteed to be available)
        MediaRequest? request = null;
        if (_mediaDevice != null)
        {
            request = Device.OutputMPlaneQueue.AcquireMediaRequest();
            SubmitFrameControls(header, isKeyFrame, request, streamState);
        }

        // Write buffer and enqueue
        Device.OutputMPlaneQueue.WriteBufferAndEnqueue(frameData, request);
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
        Device.SetSingleExtendedControl(
            V4l2ControlsConstants.V4L2_CID_STATELESS_H264_PPS,
            ppsV4L2,
            request);

        var sps = streamState.sps[pps.seq_parameter_set_id];
        var spsV4L2 = SpsMapper.MapSpsToV4L2(sps);
        Device.SetSingleExtendedControl(
            V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SPS,
            spsV4L2,
            request);

        if (_supportsSliceParamsControl)
        {
            var sliceParams = SliceParamsMapper.BuildSliceParams(header);
            Device.SetSingleExtendedControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SLICE_PARAMS,
                sliceParams,
                request);
        }

        var decodeParams = BuildDecodeParams(header, isKeyFrame, sps);
        Device.SetSingleExtendedControl(
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
            Logger.LogDebug("IDR frame detected - DPB cleared");
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
            Logger.LogTrace("Added reference frame to DPB: frame_num={FrameNum}, DPB size={Size}", header.frame_num,
                _dpb.Count);
        }

        // Manage DPB size - remove oldest frames if we exceed max size (now O(1) with Queue)
        var maxDpbSize = sps.sps_data.max_num_ref_frames;

        while (_dpb.Count > maxDpbSize)
        {
            _dpb.Dequeue(); // O(1) operation
            Logger.LogTrace("Removed oldest DPB entry, new size={Size}", _dpb.Count);
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

    public void InitializeDecoder(Action<SharedDmaBuffer>? processDecodedBufferIndex)
    {
        Logger.LogInformation("Initializing H.264 stateless decoder...");

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

        // Setup and map buffers properly with real V4L2 mmap
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

    protected override void ConfigureFormats()
    {
        Logger.LogInformation("Configuring stateless decoder formats...");

        var outputFormat = new V4L2PixFormatMplane
        {
            Width = Configuration.InitialWidth,
            Height = Configuration.InitialHeight,
            PixelFormat = V4L2PixelFormats.V4L2_PIX_FMT_H264_SLICE,
            NumPlanes = 1,
            Field = (uint)V4L2Field.NONE,
            Colorspace = 5, // V4L2_COLORSPACE_REC709
            YcbcrEncoding = 1, // V4L2_YCBCR_ENC_DEFAULT
            Quantization = 1, // V4L2_QUANTIZATION_DEFAULT
            XferFunc = 1 // V4L2_XFER_FUNC_DEFAULT
        };
        Device.SetOutputFormatMPlane(outputFormat);

        var confirmedOutputFormat = Device.GetOutputFormatMPlane();

        Logger.LogInformation(
            "Set output format: {Width}x{Height} H264 ({Planes} plane(s))",
            confirmedOutputFormat.Width,
            confirmedOutputFormat.Height,
            confirmedOutputFormat.NumPlanes);

        // Configure capture format (decoded output)
        var captureFormat = new V4L2PixFormatMplane
        {
            Width = Configuration.InitialWidth,
            Height = Configuration.InitialHeight,
            PixelFormat = Configuration.PreferredPixelFormat, // Usually NV12
            NumPlanes = 2, // NV12 typically has 2 planes
            Field = (uint)V4L2Field.NONE,
            Colorspace = 5,
            YcbcrEncoding = 1,
            Quantization = 1,
            XferFunc = 1
        };

        Device.SetCaptureFormatMPlane(captureFormat);
    }

    protected override void ConfigureDecoderControls()
    {
        // For RK3566 I can only set FRAME_BASED + ANNEX_B
        var decodeMode = V4L2StatelessH264DecodeMode.FRAME_BASED;
        if (!Device.TrySetSimpleControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_DECODE_MODE,
                (int)decodeMode))
        {
            throw new Exception($"Failed to set decode mode to {decodeMode}");
        }

        var startCode = V4L2StatelessH264StartCode.ANNEX_B;
        if (!Device.TrySetSimpleControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_START_CODE,
                (int)startCode))
        {
            throw new Exception($"Failed to set start code to {startCode}");
        }
    }

    protected override void SetupDecoderBuffers()
    {
        Logger.LogInformation("Setting up and mapping buffers...");

        // Setup OUTPUT buffers for slice data with proper V4L2 mmap
        SetupMMapBufferQueue(Device.OutputMPlaneQueue, Configuration.OutputBufferCount);
        if (_mediaDevice != null)
        {
            _mediaDevice.AllocateMediaRequests(Configuration.RequestPoolSize);
            Device.OutputMPlaneQueue.AssociateMediaRequests(_mediaDevice.OpenedRequests);
        }

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
}