using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Decoding.V4l2.H264;
using SharpVideo.Drm;
using SharpVideo.H264;
using SharpVideo.Linux.Native.V4L2;
using SharpVideo.Utils;
using SharpVideo.V4L2;

namespace SharpVideo.Decoding.V4l2.Stateless;

/// <summary>
/// V4L2 stateless H264 decoder.
/// Used for hardware decoders that require userspace to manage decoding state
/// (e.g., Raspberry Pi, Rockchip RK3588).
/// </summary>
[SupportedOSPlatform("linux")]
public class V4l2H264StatelessDecoder : BaseDecoder
{
    private readonly ILogger<V4l2H264StatelessDecoder> _logger;
    private readonly string _devicePath;
    private readonly string? _mediaDevicePath;
    private readonly V4l2DecoderConfiguration _configuration;
    private readonly DrmBufferManager? _drmBufferManager;

    private V4L2Device? _device;
    private MediaDevice? _mediaDevice;
    private List<SharedDmaBuffer>? _drmBuffers;

    private readonly bool _supportsSliceParamsControl;

    // Thread for capture buffer processing
    private Thread? _captureThread;
    private CancellationTokenSource? _captureCts;

    // DPB (Decoded Picture Buffer) tracking
    private readonly Queue<DpbEntry> _dpb = new();

    // H264 bitstream parsing state
    private readonly H264BitstreamParserState _streamState = new();
    private readonly ParsingOptions _parsingOptions = new() { add_checksum = false };

    private PixelFormat _outputPixelFormat;
    private bool _isInitialized;

    private V4l2H264StatelessDecoder(
        ILogger<V4l2H264StatelessDecoder> logger,
        string devicePath,
        string? mediaDevicePath,
        V4l2DecoderConfiguration configuration,
        DrmBufferManager? drmBufferManager)
        : base(logger)
    {
        _logger = logger;
        _devicePath = devicePath;
        _mediaDevicePath = mediaDevicePath;
        _configuration = configuration;
        _drmBufferManager = drmBufferManager;
        _outputPixelFormat = configuration.GetPixelFormat();

        // Will be set after device is opened
        _supportsSliceParamsControl = false;
    }

    /// <summary>
    /// Creates a stateless H264 decoder using the specified device.
    /// </summary>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    /// <param name="decoderInfo">Decoder information from discovery.</param>
    /// <param name="configuration">Decoder configuration settings.</param>
    /// <param name="drmBufferManager">Optional DRM buffer manager for zero-copy decoding.</param>
    /// <returns>A new stateless decoder instance.</returns>
    public static V4l2H264StatelessDecoder Create(
        ILoggerFactory loggerFactory,
        V4l2H264DecoderInfo decoderInfo,
        V4l2DecoderConfiguration? configuration = null,
        DrmBufferManager? drmBufferManager = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(decoderInfo);

        if (decoderInfo.DecoderType != V4l2H264DecoderType.Stateless)
        {
            throw new ArgumentException(
                $"Expected stateless decoder info, got {decoderInfo.DecoderType}",
                nameof(decoderInfo));
        }

        configuration ??= new V4l2DecoderConfiguration();

        if (configuration.UseDrmPrimeBuffers && drmBufferManager == null)
        {
            throw new ArgumentException(
                "DrmBufferManager is required when UseDrmPrimeBuffers is true",
                nameof(drmBufferManager));
        }

        var logger = loggerFactory.CreateLogger<V4l2H264StatelessDecoder>();
        logger.LogInformation(
            "Creating V4L2 stateless H264 decoder at {DevicePath} ({Driver}: {Card})",
            decoderInfo.DevicePath,
            decoderInfo.Driver,
            decoderInfo.Card);

        return new V4l2H264StatelessDecoder(
            logger,
            decoderInfo.DevicePath,
            decoderInfo.MediaDevicePath,
            configuration,
            drmBufferManager);
    }

    /// <summary>
    /// Gets the device path used by this decoder.
    /// </summary>
    public string DevicePath => _devicePath;

    /// <summary>
    /// Gets the media device path, if available.
    /// </summary>
    public string? MediaDevicePath => _mediaDevicePath;

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

        if (v4l2Frame.IsDmaBuf && v4l2Frame.DmaBuffer is not null)
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
        int payloadStart;

        if (encodedBuffer is V4l2EncodedBuffer v4l2Buffer)
        {
            naluData = v4l2Buffer.GetData();
            payloadStart = v4l2Buffer.NaluPayloadStart;
        }
        else if (encodedBuffer is ManagedMemoryEncodedBuffer managedBuffer)
        {
            naluData = managedBuffer.Get();
            payloadStart = managedBuffer.NaluPayloadStart;
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

        // Parse the NALU
        var naluPayload = naluData.Slice(payloadStart);
        var naluState = H264NalUnitParser.ParseNalUnit(naluPayload, _streamState, _parsingOptions);

        if (naluState == null)
        {
            _logger.LogWarning("Failed to parse NALU, skipping");
            AddEncodedBufferToReuse(encodedBuffer);
            return;
        }

        ProcessNaluByType(naluData, naluState);
        AddEncodedBufferToReuse(encodedBuffer);
    }

    /// <inheritdoc />
    protected override void FlushDecoder()
    {
        _logger.LogInformation("Flushing decoder...");
        _dpb.Clear();
    }

    private void InitializeDecoder()
    {
        _logger.LogInformation("Initializing V4L2 stateless H264 decoder at {DevicePath}", _devicePath);

        // Open V4L2 device
        _device = V4L2DeviceFactory.Open(_devicePath);
        if (_device == null)
        {
            throw new InvalidOperationException($"Failed to open V4L2 device at {_devicePath}");
        }

        // Open media device if available (required for stateless)
        if (!string.IsNullOrEmpty(_mediaDevicePath))
        {
            _mediaDevice = MediaDevice.Open(_mediaDevicePath);
            if (_mediaDevice == null)
            {
                _logger.LogWarning("Failed to open media device at {MediaDevicePath}", _mediaDevicePath);
            }
        }

        _logger.LogInformation("Device fd: {Fd}, Controls: {ControlCount}, ExtControls: {ExtControlCount}",
            _device.fd, _device.Controls.Count, _device.ExtendedControls.Count);

        // Configure formats
        ConfigureFormats();

        // Set decode mode and start code
        var decodeMode = V4L2StatelessH264DecodeMode.FRAME_BASED;
        if (!_device.TrySetSimpleControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_DECODE_MODE,
                (int)decodeMode))
        {
            throw new InvalidOperationException($"Failed to set decode mode to {decodeMode}");
        }

        var startCode = V4L2StatelessH264StartCode.ANNEX_B;
        if (!_device.TrySetSimpleControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_START_CODE,
                (int)startCode))
        {
            throw new InvalidOperationException($"Failed to set start code to {startCode}");
        }

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
            NumPlanes = 2,
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
            Name = "V4L2CaptureBufferProcessor",
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

    private void ProcessNaluByType(ReadOnlySpan<byte> naluData, NalUnitState naluState)
    {
        var naluType = (NalUnitType)naluState.nal_unit_header.nal_unit_type;

        switch (naluType)
        {
            case NalUnitType.SPS_NUT:
                var spsData = naluState.nal_unit_payload.sps?.sps_data;
                if (spsData != null)
                {
                    _logger.LogInformation(
                        "SPS RECEIVED: id={SpsId}, profile={Profile}, level={Level}, size={Width}x{Height}",
                        spsData.seq_parameter_set_id,
                        spsData.profile_idc,
                        spsData.level_idc,
                        (spsData.pic_width_in_mbs_minus1 + 1) * 16,
                        (spsData.pic_height_in_map_units_minus1 + 1) * 16);
                }
                break;

            case NalUnitType.PPS_NUT:
                var ppsData = naluState.nal_unit_payload.pps;
                if (ppsData != null)
                {
                    _logger.LogInformation(
                        "PPS RECEIVED: id={PpsId}, references SPS={SpsId}",
                        ppsData.pic_parameter_set_id,
                        ppsData.seq_parameter_set_id);
                }
                break;

            case NalUnitType.CODED_SLICE_OF_NON_IDR_PICTURE_NUT:
            case NalUnitType.CODED_SLICE_OF_IDR_PICTURE_NUT:
                _logger.LogTrace("Processing slice NALU type {NaluType}", naluType);
                var sliceData = naluState.nal_unit_payload.slice_layer_without_partitioning_rbsp;
                if (sliceData == null)
                {
                    _logger.LogWarning("Failed to parse slice data for NALU type {NaluType}, skipping", naluType);
                    break;
                }
                HandleSliceNalu(naluData, sliceData, naluType);
                break;

            default:
                _logger.LogTrace("Skipping NALU type {NaluType}", naluType);
                break;
        }
    }

    private void HandleSliceNalu(
        ReadOnlySpan<byte> naluData,
        SliceLayerWithoutPartitioningRbspState sliceData,
        NalUnitType naluType)
    {
        var header = sliceData.slice_header;
        if (header.first_mb_in_slice != 0)
        {
            _logger.LogDebug("Skipping non-initial slice for frame {FrameNum}", header.frame_num);
            return;
        }

        if (!_streamState.pps.TryGetValue(header.pic_parameter_set_id, out var pps) || pps == null)
        {
            _logger.LogWarning("Cannot decode slice: PPS {PpsId} not received yet", header.pic_parameter_set_id);
            return;
        }

        if (!_streamState.sps.TryGetValue(pps.seq_parameter_set_id, out var sps) || sps == null)
        {
            _logger.LogWarning("Cannot decode slice: SPS {SpsId} not received yet", pps.seq_parameter_set_id);
            return;
        }

        var isKeyFrame = naluType == NalUnitType.CODED_SLICE_OF_IDR_PICTURE_NUT;
        SubmitFrameToDevice(naluData, header, isKeyFrame, sps, pps);
    }

    private void SubmitFrameToDevice(
        ReadOnlySpan<byte> frameData,
        SliceHeaderState header,
        bool isKeyFrame,
        SpsState sps,
        PpsState pps)
    {
        _device!.OutputMPlaneQueue.EnsureFreeBuffer();

        MediaRequest? request = null;
        if (_mediaDevice != null)
        {
            request = _device.OutputMPlaneQueue.AcquireMediaRequest();
            SubmitFrameControls(header, isKeyFrame, request, sps, pps);
        }

        _device.OutputMPlaneQueue.WriteBufferAndEnqueue(frameData, request);
        request?.Queue();
    }

    private void SubmitFrameControls(
        SliceHeaderState header,
        bool isKeyFrame,
        MediaRequest request,
        SpsState sps,
        PpsState pps)
    {
        var ppsV4L2 = PpsMapper.ConvertPpsStateToV4L2(pps);
        _device!.SetSingleExtendedControl(
            V4l2ControlsConstants.V4L2_CID_STATELESS_H264_PPS,
            ppsV4L2,
            request);

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
        if (isIdr)
        {
            _dpb.Clear();
            _logger.LogDebug("IDR frame detected - DPB cleared");
        }

        var dpbArray = CreateEmptyDpb();

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
            _logger.LogTrace("Added reference frame to DPB: frame_num={FrameNum}, DPB size={Size}",
                header.frame_num, _dpb.Count);
        }

        var maxDpbSize = sps.sps_data.max_num_ref_frames;
        while (_dpb.Count > maxDpbSize)
        {
            _dpb.Dequeue();
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

        _mediaDevice?.Dispose();
        _mediaDevice = null;

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