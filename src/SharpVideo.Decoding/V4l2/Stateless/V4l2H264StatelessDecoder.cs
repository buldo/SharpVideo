using System.Collections.Concurrent;
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
/// Implementation follows GStreamer's gstv4l2codech264dec.c as the reference.
/// Supports frame-based and slice-based decoding modes.
/// </summary>
[SupportedOSPlatform("linux")]
public class V4l2H264StatelessDecoder : BaseDecoder<SharedDmaBuffer>
{
    private readonly V4L2Device _device;
    private readonly MediaDevice _mediaDevice;
    private readonly ILogger<V4l2H264StatelessDecoder> _logger;
    private readonly V4l2DecoderConfiguration _configuration;
    private readonly DrmBufferManager _drmBufferManager;

    // Buffer management
    private List<SharedDmaBuffer>? _drmBuffers;
    private Dictionary<uint, SharedDmaBuffer>? _v4l2IndexToBuffer;
    private readonly BlockingCollection<SharedDmaBuffer> _availableCaptureBuffers = new();
    private readonly HashSet<SharedDmaBuffer> _pendingReuse = new();

    // DPB and picture state
    private readonly H264Dpb _dpb;
    private readonly H264PicOrderCountCalculator _pocCalculator = new();
    private H264Picture? _currentPicture;
    private readonly Dictionary<SharedDmaBuffer, H264Picture> _bufferToPicture = new();
    private readonly object _dpbLock = new();

    // Decode mode and start code (determined at open time like GStreamer)
    private V4L2StatelessH264DecodeMode _decodeMode;
    private V4L2StatelessH264StartCode _startCode;

    // Control support detection
    private bool _supportsSliceParams;
    private bool _supportsScalingMatrix;
    private bool _supportsPredWeights;

    // Frame/sequence state (matching GStreamer fields)
    private uint _displayWidth;
    private uint _displayHeight;
    private uint _codedWidth;
    private uint _codedHeight;
    private uint _bitdepth;
    private uint _chromaFormatIdc;
    private int _minPoolSize;
    private bool _interlaced;
    private bool _needSequence;
    private bool _scalingMatrixPresent;

    // V4L2 control structures (following GStreamer naming)
    private V4L2CtrlH264Sps _sps;
    private V4L2CtrlH264Pps _pps;
    private V4L2CtrlH264ScalingMatrix _scalingMatrix;
    private V4L2CtrlH264DecodeParams _decodeParams;
    private V4L2CtrlH264PredWeights _predWeights;
    private readonly List<V4L2CtrlH264SliceParams> _sliceParams = new();

    // Slice tracking for multi-slice frames
    private int _numSlices;
    private bool _firstSlice;

    // Bitstream assembly
    private MemoryStream? _bitstreamBuffer;
    private SpsState? _currentSps;
    private PpsState? _currentPps;
    private SliceHeaderState? _currentSliceHeader;
    private bool _currentIsIdr;

    // System frame counter (following GStreamer: system_frame_number used to generate reference_ts)
    private uint _systemFrameNumber;

    // H264 bitstream parsing state
    private readonly H264BitstreamParserState _streamState = new();
    private readonly ParsingOptions _parsingOptions = new() { add_checksum = false };

    // Streaming state
    private bool _isInitialized;
    private bool _streaming;

    // Capture buffer processing thread
    private Thread? _captureThread;
    private CancellationTokenSource? _captureCts;

    private V4l2H264StatelessDecoder(
        V4L2Device device,
        MediaDevice mediaDevice,
        V4l2DecoderConfiguration configuration,
        DrmBufferManager drmBufferManager,
        ILogger<V4l2H264StatelessDecoder> logger)
        : base(logger)
    {
        _logger = logger;
        _device = device;
        _mediaDevice = mediaDevice;
        _configuration = configuration;
        _drmBufferManager = drmBufferManager;
        _dpb = new H264Dpb(logger);

        OutputPixelFormat = new PixelFormat(_device.GetCaptureFormatMPlane().PixelFormat);
    }

    /// <summary>
    /// Creates a new V4L2 H264 stateless decoder instance.
    /// </summary>
    public static V4l2H264StatelessDecoder Create(
        V4L2Device device,
        MediaDevice mediaDevice,
        ILoggerFactory loggerFactory,
        V4l2DecoderConfiguration? configuration,
        DrmBufferManager drmBufferManager)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(drmBufferManager);

        configuration ??= new V4l2DecoderConfiguration();
        var logger = loggerFactory.CreateLogger<V4l2H264StatelessDecoder>();

        return new V4l2H264StatelessDecoder(
            device,
            mediaDevice,
            configuration,
            drmBufferManager,
            logger);
    }

    /// <inheritdoc />
    public override PixelFormat OutputPixelFormat { get; }

    /// <inheritdoc />
    public override void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        Open();
        _isInitialized = true;
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
            return;
        }

        int startCodeLength = GetStartCodeLength(nalu);
        if (startCodeLength == 0 || nalu.Length <= startCodeLength)
        {
            return;
        }

        var naluState = H264NalUnitParser.ParseNalUnit(
            nalu.Slice(startCodeLength), _streamState, _parsingOptions);
        if (naluState == null)
        {
            _logger.LogWarning("Failed to parse NALU");
            return;
        }

        switch (naluState.nal_unit_header.NalUnitType)
        {
            case NalUnitType.SPS_NUT:
                ProcessSps(naluState);
                break;

            case NalUnitType.PPS_NUT:
                ProcessPps(naluState);
                break;

            case NalUnitType.CODED_SLICE_OF_NON_IDR_PICTURE_NUT:
            case NalUnitType.CODED_SLICE_OF_IDR_PICTURE_NUT:
                ProcessSlice(nalu, naluState, naluState.nal_unit_header.NalUnitType);
                break;

            default:
                _logger.LogTrace("Skipping NALU type {NaluType}", naluState.nal_unit_header.NalUnitType);
                break;
        }
    }

    /// <inheritdoc />
    public override void ReuseDecodedFrame(SharedDmaBuffer decodedFrame)
    {
        lock (_dpbLock)
        {
            if (_bufferToPicture.TryGetValue(decodedFrame, out var picture) && picture.IsRef)
            {
                // Buffer is still used as reference, mark for pending reuse
                _pendingReuse.Add(decodedFrame);
                _logger.LogTrace("Buffer still referenced in DPB, deferring reuse");
                return;
            }

            _pendingReuse.Remove(decodedFrame);
            _bufferToPicture.Remove(decodedFrame);
        }

        decodedFrame.V4L2Buffer.ResetPlanesUsed();
        _availableCaptureBuffers.Add(decodedFrame);
    }

    /// <inheritdoc />
    protected override void FlushDecoder()
    {
        _logger.LogInformation("Flushing decoder...");

        // Submit any pending picture
        EndPicture();
        ResetBitstream();

        lock (_dpbLock)
        {
            foreach (var buffer in _pendingReuse)
            {
                buffer.V4L2Buffer.ResetPlanesUsed();
                _availableCaptureBuffers.Add(buffer);
            }
            _pendingReuse.Clear();

            foreach (var pic in _dpb.GetPictures())
            {
                if (pic.Buffer != null)
                {
                    _bufferToPicture.Remove(pic.Buffer);
                }
            }
            _dpb.Clear();
            _bufferToPicture.Clear();
        }

        _currentPicture = null;
        _pocCalculator.Reset();
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

    // ============================================
    // OPEN - Following gst_v4l2_codec_h264_dec_open
    // ============================================

    private void Open()
    {
        _logger.LogInformation("Opening H.264 stateless decoder...");

        // Query decode mode and start code (like GStreamer's gst_v4l2_codec_h264_dec_open)
        if (!QueryDecodeMode())
        {
            throw new InvalidOperationException("Failed to query decode mode");
        }

        _logger.LogInformation("Opened H264 {Mode} decoder {StartCode}",
            IsFrameBased() ? "frame-based" : "slice-based",
            NeedsStartCodes() ? "using start-codes" : "without start-codes");

        // Detect control support
        DetectControlSupport();

        // Configure formats and allocate buffers
        Negotiate();
    }

    private bool QueryDecodeMode()
    {
        // Try to get decode mode
        if (!_device.TrySetSimpleControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_DECODE_MODE,
                (int)V4L2StatelessH264DecodeMode.FRAME_BASED))
        {
            _logger.LogWarning("Failed to set frame-based decode mode");
            return false;
        }
        _decodeMode = V4L2StatelessH264DecodeMode.FRAME_BASED;

        // Try to set start code
        if (!_device.TrySetSimpleControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_START_CODE,
                (int)V4L2StatelessH264StartCode.ANNEX_B))
        {
            _logger.LogWarning("Failed to set ANNEX_B start code");
            return false;
        }
        _startCode = V4L2StatelessH264StartCode.ANNEX_B;

        return true;
    }

    private void DetectControlSupport()
    {
        _supportsSliceParams = _device.ExtendedControls.Any(
            c => c.Id == V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SLICE_PARAMS);
        _supportsScalingMatrix = _device.ExtendedControls.Any(
            c => c.Id == V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SCALING_MATRIX);
        _supportsPredWeights = _device.ExtendedControls.Any(
            c => c.Id == V4l2ControlsConstants.V4L2_CID_STATELESS_H264_PRED_WEIGHTS);

        _logger.LogInformation("Control support: SliceParams={SliceParams}, ScalingMatrix={ScalingMatrix}, PredWeights={PredWeights}",
            _supportsSliceParams, _supportsScalingMatrix, _supportsPredWeights);
    }

    private bool IsFrameBased() => _decodeMode == V4L2StatelessH264DecodeMode.FRAME_BASED;
    private bool IsSliceBased() => _decodeMode == V4L2StatelessH264DecodeMode.SLICE_BASED;
    private bool NeedsStartCodes() => _startCode == V4L2StatelessH264StartCode.ANNEX_B;

    // ============================================
    // NEGOTIATE - Following gst_v4l2_codec_h264_dec_negotiate
    // ============================================

    private void Negotiate()
    {
        if (_streaming)
        {
            return;
        }

        _logger.LogInformation("Negotiating decoder formats...");

        // Set sink format (encoded input)
        var outputFormat = new V4L2PixFormatMplane
        {
            Width = _configuration.InitialWidth,
            Height = _configuration.InitialHeight,
            PixelFormat = V4L2PixelFormats.V4L2_PIX_FMT_H264_SLICE,
            NumPlanes = 1,
            Field = (uint)V4L2Field.NONE,
            Colorspace = 5,
            YcbcrEncoding = 1,
            Quantization = 1,
            XferFunc = 1
        };
        _device.SetOutputFormatMPlane(outputFormat);

        var confirmedOutputFormat = _device.GetOutputFormatMPlane();
        _logger.LogInformation("Set output format: {Width}x{Height} H264_SLICE",
            confirmedOutputFormat.Width, confirmedOutputFormat.Height);

        // Set capture format (decoded output)
        var captureFormat = new V4L2PixFormatMplane
        {
            Width = _configuration.InitialWidth,
            Height = _configuration.InitialHeight,
            PixelFormat = OutputPixelFormat.Fourcc,
            NumPlanes = 2,
            Field = (uint)V4L2Field.NONE,
            Colorspace = 5,
            YcbcrEncoding = 1,
            Quantization = 1,
            XferFunc = 1
        };
        _device.SetCaptureFormatMPlane(captureFormat);

        SetupBuffers();
    }

    private void SetupBuffers()
    {
        _logger.LogInformation("Setting up buffers...");

        // Setup OUTPUT buffers (MMAP for encoded data)
        _device.OutputMPlaneQueue.InitMMap(_configuration.OutputBufferCount);
        foreach (var buffer in _device.OutputMPlaneQueue.BuffersPool.Buffers)
        {
            buffer.MapToMemory();
        }

        _mediaDevice.AllocateMediaRequests(_configuration.RequestPoolSize);
        _device.OutputMPlaneQueue.AssociateMediaRequests(_mediaDevice.OpenedRequests);

        // Setup CAPTURE buffers (DMABUF for decoded frames)
        SetupDmaBufCaptureQueue();
    }

    private void SetupDmaBufCaptureQueue()
    {
        _logger.LogInformation("Setting up DMABUF capture queue");
        var negotiatedFormat = _device.GetCaptureFormatMPlane();

        if (negotiatedFormat.NumPlanes != 1)
        {
            throw new InvalidOperationException("Only 1-plane DMABUF is supported");
        }

        _drmBuffers = _drmBufferManager.AllocateFromFormat(
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

        _v4l2IndexToBuffer = new Dictionary<uint, SharedDmaBuffer>();
        foreach (var buffer in _drmBuffers)
        {
            buffer.V4L2Buffer = _device.CaptureMPlaneQueue.DmaBufBuffersPool.Buffers
                .Single(b => b.DmaBufferFd == buffer.DmaBuffer.Fd);
            _v4l2IndexToBuffer[buffer.V4L2Buffer.Index] = buffer;
            buffer.V4L2Buffer.ResetPlanesUsed();
            _availableCaptureBuffers.Add(buffer);
        }
    }

    private void StartStreaming()
    {
        if (_streaming)
        {
            return;
        }

        _logger.LogInformation("Starting V4L2 streaming...");
        _device.OutputMPlaneQueue.StreamOn();
        _device.CaptureMPlaneQueue.StreamOn();

        _captureCts = new CancellationTokenSource();
        _captureThread = new Thread(CaptureBufferProcessingLoop)
        {
            Name = "V4L2CaptureProcessor",
            IsBackground = true
        };
        _captureThread.Start();

        _streaming = true;
    }

    private void StopStreaming()
    {
        if (!_streaming)
        {
            return;
        }

        _logger.LogInformation("Stopping V4L2 streaming...");

        _captureCts?.Cancel();
        if (_captureThread is { IsAlive: true })
        {
            _captureThread.Join(TimeSpan.FromSeconds(2));
        }
        _captureCts?.Dispose();
        _captureCts = null;
        _captureThread = null;

        _device.OutputMPlaneQueue.StreamOff();
        _device.CaptureMPlaneQueue.StreamOff();

        _streaming = false;
    }

    private void CaptureBufferProcessingLoop()
    {
        var ct = _captureCts!.Token;
        _logger.LogInformation("Capture buffer processing started");

        while (!ct.IsCancellationRequested)
        {
            var dequeuedBuffer = _device.CaptureMPlaneQueue.WaitForReadyBuffer(1000);
            if (dequeuedBuffer == null)
            {
                continue;
            }

            var decodedFrame = _v4l2IndexToBuffer![dequeuedBuffer.Index];
            AddDecodedFrameToOutput(decodedFrame);
        }

        _logger.LogInformation("Capture buffer processing stopped");
    }

    // ============================================
    // NEW_SEQUENCE - Following gst_v4l2_codec_h264_dec_new_sequence
    // ============================================

    private void ProcessSps(NalUnitState naluState)
    {
        var spsData = naluState.nal_unit_payload.sps?.sps_data;
        if (spsData == null)
        {
            return;
        }

        _logger.LogInformation("SPS: id={SpsId}, profile={Profile}, level={Level}, size={Width}x{Height}, max_refs={MaxRefs}",
            spsData.seq_parameter_set_id,
            spsData.profile_idc,
            spsData.level_idc,
            (spsData.pic_width_in_mbs_minus1 + 1) * 16,
            (spsData.pic_height_in_map_units_minus1 + 1) * 16,
            spsData.max_num_ref_frames);

        // Following gst_v4l2_codec_h264_dec_new_sequence
        bool negotiationNeeded = false;

        // Check if we need to update pool size
        int maxDpbSize = (int)spsData.max_num_ref_frames + 1;
        if (_minPoolSize < maxDpbSize)
        {
            _minPoolSize = maxDpbSize;
            negotiationNeeded = true;
        }

        // Calculate dimensions using getResolution which handles cropping
        spsData.getResolution(out int cropWidth, out int cropHeight);
        uint codedWidth = (uint)((spsData.pic_width_in_mbs_minus1 + 1) * 16);
        uint codedHeight = (uint)((spsData.pic_height_in_map_units_minus1 + 1) * 16);

        if (_displayWidth != (uint)cropWidth || _displayHeight != (uint)cropHeight ||
            _codedWidth != codedWidth || _codedHeight != codedHeight)
        {
            _displayWidth = (uint)cropWidth;
            _displayHeight = (uint)cropHeight;
            _codedWidth = codedWidth;
            _codedHeight = codedHeight;
            negotiationNeeded = true;
            _logger.LogInformation("Resolution changed to {DisplayW}x{DisplayH} ({CodedW}x{CodedH})",
                _displayWidth, _displayHeight, _codedWidth, _codedHeight);
        }

        bool interlaced = spsData.frame_mbs_only_flag == 0;
        if (_interlaced != interlaced)
        {
            _interlaced = interlaced;
            negotiationNeeded = true;
            _logger.LogInformation("Interlaced mode changed to {Interlaced}", interlaced);
        }

        uint bitdepth = spsData.bit_depth_luma_minus8 + 8;
        if (_bitdepth != bitdepth)
        {
            _bitdepth = bitdepth;
            negotiationNeeded = true;
            _logger.LogInformation("Bitdepth changed to {Bitdepth}", _bitdepth);
        }

        if (_chromaFormatIdc != spsData.chroma_format_idc)
        {
            _chromaFormatIdc = spsData.chroma_format_idc;
            negotiationNeeded = true;
            _logger.LogInformation("Chroma format changed to {ChromaFormat}", _chromaFormatIdc);
        }

        // Fill SPS control structure (gst_v4l2_codec_h264_dec_fill_sequence)
        FillSequence(naluState.nal_unit_payload.sps!);
        _needSequence = true;

        // Update DPB max ref frames
        var maxRefs = (int)spsData.max_num_ref_frames;
        var maxAvailableRefs = (int)_configuration.CaptureBufferCount - 1;
        if (maxRefs > maxAvailableRefs)
        {
            _logger.LogWarning("max_num_ref_frames ({MaxRefs}) exceeds available buffers ({Available}), clamping",
                maxRefs, maxAvailableRefs);
            maxRefs = maxAvailableRefs;
        }
        _dpb.SetMaxNumRefFrames(maxRefs);

        if (negotiationNeeded)
        {
            StopStreaming();
            // Re-negotiate would go here if we supported dynamic resolution changes
        }
    }

    private void FillSequence(SpsState sps)
    {
        // Following gst_v4l2_codec_h264_dec_fill_sequence
        _sps = SpsMapper.MapSpsToV4L2(sps);
    }

    private void ProcessPps(NalUnitState naluState)
    {
        var ppsData = naluState.nal_unit_payload.pps;
        if (ppsData != null)
        {
            _logger.LogTrace("PPS: id={PpsId}, references SPS={SpsId}",
                ppsData.pic_parameter_set_id, ppsData.seq_parameter_set_id);
        }
    }

    // ============================================
    // START_PICTURE - Following gst_v4l2_codec_h264_dec_start_picture
    // ============================================

    private void StartPicture(SliceHeaderState sliceHeader, PpsState pps, SpsState sps, bool isIdr)
    {
        EnsureBitstream();

        // Determine scaling_matrix_present (GStreamer does this here)
        _scalingMatrixPresent = sps.sps_data.seq_scaling_matrix_present_flag != 0 ||
                                pps.pic_scaling_matrix_present_flag != 0;

        // Fill PPS (gst_v4l2_codec_h264_dec_fill_pps)
        FillPps(pps);

        // Fill scaling matrix if present
        if (_scalingMatrixPresent)
        {
            FillScalingMatrix(sps, pps);
        }

        // Create picture and fill decode params
        _currentPicture = CreatePicture(sliceHeader, sps, isIdr);
        FillDecodeParams(sliceHeader, _currentPicture);

        _currentSps = sps;
        _currentPps = pps;
        _currentSliceHeader = sliceHeader;
        _currentIsIdr = isIdr;

        _firstSlice = true;
        _numSlices = 0;
    }

    private void EnsureBitstream()
    {
        _bitstreamBuffer ??= new MemoryStream();
        _bitstreamBuffer.SetLength(0);
    }

    private H264Picture CreatePicture(SliceHeaderState header, SpsState sps, bool isIdr)
    {
        _systemFrameNumber++;

        var picture = new H264Picture
        {
            SystemFrameNumber = _systemFrameNumber
        };
        picture.InitFromSliceHeader(header, sps, isIdr);

        // For IDR, clear DPB and reset POC
        if (isIdr)
        {
            lock (_dpbLock)
            {
                foreach (var buffer in _pendingReuse)
                {
                    buffer.V4L2Buffer.ResetPlanesUsed();
                    _availableCaptureBuffers.Add(buffer);
                }
                _pendingReuse.Clear();

                _dpb.MarkAllNonRef();
                foreach (var pic in _dpb.GetPictures())
                {
                    if (pic.Buffer != null)
                    {
                        _bufferToPicture.Remove(pic.Buffer);
                    }
                }
                _dpb.Clear();
                _bufferToPicture.Clear();
            }
            _pocCalculator.Reset();
        }

        // Calculate POC
        var topPoc = _pocCalculator.CalculatePOC(header, sps, isIdr);
        picture.TopFieldOrderCnt = topPoc;
        picture.BottomFieldOrderCnt = topPoc + header.delta_pic_order_cnt_bottom;

        _logger.LogDebug("Picture: sys_frame={SysFrame}, frame_num={FrameNum}, POC={Poc}, IsRef={IsRef}",
            picture.SystemFrameNumber, picture.FrameNum, picture.GetPicOrderCnt(), picture.IsRef);

        return picture;
    }

    private void FillPps(PpsState pps)
    {
        // Following gst_v4l2_codec_h264_dec_fill_pps
        _pps = PpsMapper.ConvertPpsStateToV4L2(pps);

        // Set scaling matrix present flag
        if (_scalingMatrixPresent)
        {
            _pps.Flags |= 0x80; // V4L2_H264_PPS_FLAG_SCALING_MATRIX_PRESENT
        }
    }

    private void FillScalingMatrix(SpsState sps, PpsState pps)
    {
        // Following gst_v4l2_codec_h264_dec_fill_scaling_matrix
        _scalingMatrix = ScalingMatrixMapper.MapScalingMatrix(sps, pps);
    }

    private void FillDecodeParams(SliceHeaderState sliceHeader, H264Picture picture)
    {
        // Following gst_v4l2_codec_h264_dec_fill_decoder_params
        V4L2H264DpbEntry[] dpbSnapshot;
        lock (_dpbLock)
        {
            dpbSnapshot = _dpb.CreateV4L2Dpb();
        }

        _decodeParams = new V4L2CtrlH264DecodeParams
        {
            Dpb = dpbSnapshot,
            NalRefIdc = picture.NalRefIdc,
            FrameNum = (ushort)Math.Min(sliceHeader.frame_num, ushort.MaxValue),
            IdrPicId = (ushort)Math.Min(sliceHeader.idr_pic_id, ushort.MaxValue),
            PicOrderCntLsb = (ushort)Math.Min(sliceHeader.pic_order_cnt_lsb, ushort.MaxValue),
            DeltaPicOrderCntBottom = sliceHeader.delta_pic_order_cnt_bottom,
            DeltaPicOrderCnt0 = sliceHeader.delta_pic_order_cnt.Count > 0 ? sliceHeader.delta_pic_order_cnt[0] : 0,
            DeltaPicOrderCnt1 = sliceHeader.delta_pic_order_cnt.Count > 1 ? sliceHeader.delta_pic_order_cnt[1] : 0,
            DecRefPicMarkingBitSize = sliceHeader.dec_ref_pic_marking?.bit_size ?? 0,
            PicOrderCntBitSize = SliceHeaderState.getPicOrderCntLsbLen(_currentSps?.sps_data.log2_max_pic_order_cnt_lsb_minus4 ?? 0),
            SliceGroupChangeCycle = sliceHeader.slice_group_change_cycle,
            Reserved = 0,
            Flags = (picture.IsIdr ? V4L2H264Constants.V4L2_H264_DECODE_PARAM_FLAG_IDR_PIC : 0u) |
                    (sliceHeader.field_pic_flag != 0 ? V4L2H264Constants.V4L2_H264_DECODE_PARAM_FLAG_FIELD_PIC : 0u) |
                    (sliceHeader.bottom_field_flag != 0 ? V4L2H264Constants.V4L2_H264_DECODE_PARAM_FLAG_BOTTOM_FIELD : 0u)
        };

        // Set field order counts based on picture field (matching GStreamer)
        switch (picture.Field)
        {
            case H264PictureField.Frame:
                _decodeParams.TopFieldOrderCnt = picture.TopFieldOrderCnt;
                _decodeParams.BottomFieldOrderCnt = picture.BottomFieldOrderCnt;
                break;
            case H264PictureField.TopField:
                _decodeParams.TopFieldOrderCnt = picture.TopFieldOrderCnt;
                _decodeParams.BottomFieldOrderCnt = picture.OtherField?.BottomFieldOrderCnt ?? 0;
                break;
            case H264PictureField.BottomField:
                _decodeParams.TopFieldOrderCnt = picture.OtherField?.TopFieldOrderCnt ?? 0;
                _decodeParams.BottomFieldOrderCnt = picture.BottomFieldOrderCnt;
                break;
        }
    }

    // ============================================
    // DECODE_SLICE - Following gst_v4l2_codec_h264_dec_decode_slice
    // ============================================

    private void ProcessSlice(ReadOnlySpan<byte> nalu, NalUnitState naluState, NalUnitType naluType)
    {
        var sliceData = naluState.nal_unit_payload.slice_layer_without_partitioning_rbsp;
        if (sliceData == null)
        {
            _logger.LogWarning("Failed to parse slice data");
            return;
        }

        var header = sliceData.slice_header;

        // Get PPS and SPS
        if (!_streamState.pps.TryGetValue(header.pic_parameter_set_id, out var pps) || pps == null)
        {
            _logger.LogWarning("PPS {PpsId} not available, skipping slice", header.pic_parameter_set_id);
            return;
        }

        if (!_streamState.sps.TryGetValue(pps.seq_parameter_set_id, out var sps) || sps == null)
        {
            _logger.LogWarning("SPS {SpsId} not available, skipping slice", pps.seq_parameter_set_id);
            return;
        }

        bool isIdr = naluType == NalUnitType.CODED_SLICE_OF_IDR_PICTURE_NUT;

        // Check if this is the first slice of a new picture
        if (header.first_mb_in_slice == 0)
        {
            // End previous picture if any
            EndPicture();

            // Start new picture
            StartPicture(header, pps, sps, isIdr);
        }
        else if (_currentPicture == null)
        {
            _logger.LogWarning("Received non-first slice without active picture");
            return;
        }

        // Decode the slice
        DecodeSlice(nalu, header);
    }

    private void DecodeSlice(ReadOnlySpan<byte> nalu, SliceHeaderState sliceHeader)
    {
        if (_currentPicture == null || _bitstreamBuffer == null)
        {
            return;
        }

        // For slice-based mode: submit pending slice with hold flag
        if (IsSliceBased() && _bitstreamBuffer.Length > 0)
        {
            SubmitBitstream(V4L2BufFlags.M2M_HOLD_CAPTURE_BUF);
            EnsureBitstream();
        }

        // Fill slice params for slice-based mode
        if (IsSliceBased())
        {
            FillSliceParams(sliceHeader);
            FillPredWeight(sliceHeader);
            FillReferences(sliceHeader);
        }

        // Copy NAL data with start codes if needed
        if (NeedsStartCodes())
        {
            _bitstreamBuffer.WriteByte(0x00);
            _bitstreamBuffer.WriteByte(0x00);
            _bitstreamBuffer.WriteByte(0x01);
        }

        // Write the NAL unit (skip start code already in input)
        int startCodeLen = GetStartCodeLength(nalu);
        _bitstreamBuffer.Write(nalu.Slice(startCodeLen));

        // Update decode params flags based on slice type (GStreamer accumulates with |=)
        uint sliceType = sliceHeader.slice_type % 5;
        switch (sliceType)
        {
            case 0: // P slice
            case 3: // SP slice
                _decodeParams.Flags |= V4L2H264Constants.V4L2_H264_DECODE_PARAM_FLAG_PFRAME;
                break;
            case 1: // B slice
                _decodeParams.Flags |= V4L2H264Constants.V4L2_H264_DECODE_PARAM_FLAG_BFRAME;
                break;
        }

        _numSlices++;
    }

    private void FillSliceParams(SliceHeaderState header)
    {
        // Following gst_v4l2_codec_h264_dec_fill_slice_params
        var sliceParams = SliceParamsMapper.BuildSliceParams(header, _currentPps!, _decodeParams.Dpb);
        _sliceParams.Add(sliceParams);
    }

    private void FillPredWeight(SliceHeaderState header)
    {
        // Following gst_v4l2_codec_h264_dec_fill_pred_weight
        if (!_supportsPredWeights || header.pred_weight_table == null)
        {
            return;
        }

        _predWeights = PredWeightMapper.MapPredWeights(header);
    }

    private void FillReferences(SliceHeaderState header)
    {
        // This would fill ref_pic_list0 and ref_pic_list1 in slice params
        // For slice-based mode following gst_v4l2_codec_h264_dec_fill_references
        // Currently handled in SliceParamsMapper
    }

    // ============================================
    // END_PICTURE - Following gst_v4l2_codec_h264_dec_end_picture
    // ============================================

    private void EndPicture()
    {
        if (_currentPicture == null || _bitstreamBuffer == null || _bitstreamBuffer.Length == 0)
        {
            return;
        }

        // For interlaced first field, use hold flag
        uint flags = 0;
        if (_currentPicture.Field != H264PictureField.Frame && !_currentPicture.SecondField)
        {
            flags = V4L2BufFlags.M2M_HOLD_CAPTURE_BUF;
        }

        SubmitBitstream(flags);
        ResetBitstream();
    }

    private void ResetBitstream()
    {
        _bitstreamBuffer?.Dispose();
        _bitstreamBuffer = null;
        _currentPicture = null;
        _currentSps = null;
        _currentPps = null;
        _currentSliceHeader = null;
        _numSlices = 0;
        _sliceParams.Clear();
    }

    // ============================================
    // SUBMIT_BITSTREAM - Following gst_v4l2_codec_h264_dec_submit_bitstream
    // ============================================

    private void SubmitBitstream(uint flags = 0)
    {
        if (_currentPicture == null || _bitstreamBuffer == null || _bitstreamBuffer.Length == 0)
        {
            return;
        }

        // Ensure OUTPUT buffer is available
        _device.OutputMPlaneQueue.EnsureFreeBuffer();

        // Acquire capture buffer
        var captureBuffer = AcquireCaptureBuffer();
        captureBuffer.V4L2Buffer.ResetPlanesUsed();

        _currentPicture.Buffer = captureBuffer;

        // Acquire media request
        var request = _device.OutputMPlaneQueue.AcquireMediaRequest();

        // Set controls in proper order (following GStreamer):
        // 1. SPS (if need_sequence)
        // 2. PPS
        // 3. SCALING_MATRIX (if present)
        // 4. DECODE_PARAMS
        // 5. For slice-based: SLICE_PARAMS + PRED_WEIGHTS

        if (_needSequence)
        {
            _device.SetSingleExtendedControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SPS,
                _sps,
                request);
            _needSequence = false;
        }

        if (_firstSlice)
        {
            _device.SetSingleExtendedControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_PPS,
                _pps,
                request);

            if (_scalingMatrixPresent && _supportsScalingMatrix)
            {
                _device.SetSingleExtendedControl(
                    V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SCALING_MATRIX,
                    _scalingMatrix,
                    request);
            }

            _device.SetSingleExtendedControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_DECODE_PARAMS,
                _decodeParams,
                request);

            _firstSlice = false;
        }

        // For slice-based mode, set slice params and pred weights
        if (IsSliceBased() && _supportsSliceParams && _sliceParams.Count > 0)
        {
            // TODO: Set array of slice params
            // For now, set first slice params only
            _device.SetSingleExtendedControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SLICE_PARAMS,
                _sliceParams[0],
                request);

            if (_supportsPredWeights)
            {
                _device.SetSingleExtendedControl(
                    V4l2ControlsConstants.V4L2_CID_STATELESS_H264_PRED_WEIGHTS,
                    _predWeights,
                    request);
            }
        }

        // Timestamp for OUTPUT buffer
        var timestamp = new TimeVal
        {
            TvSec = (nint)(_currentPicture.SystemFrameNumber / 1_000_000),
            TvUsec = (nint)(_currentPicture.SystemFrameNumber % 1_000_000)
        };

        // Queue capture buffer
        _device.CaptureMPlaneQueue.EnqueueDmaBufBuffer(captureBuffer.V4L2Buffer, request, null);

        // Write bitstream and queue output buffer
        var bitstreamData = _bitstreamBuffer.ToArray();
        _device.OutputMPlaneQueue.WriteBufferAndEnqueue(bitstreamData, request, timestamp);

        // Queue the media request
        request.Queue();

        StartStreaming();

        // Add picture to DPB if it's a reference
        if (_currentPicture.IsRef)
        {
            AddPictureToDpb(_currentPicture, captureBuffer);
        }

        _sliceParams.Clear();
    }

    private void AddPictureToDpb(H264Picture picture, SharedDmaBuffer buffer)
    {
        lock (_dpbLock)
        {
            // Perform sliding window marking before adding
            _dpb.PerformSlidingWindowMarking(_dpb.MaxNumRefFrames);

            // Remove unused pictures
            var removedPictures = _dpb.RemoveUnusedPictures();
            foreach (var removedPic in removedPictures)
            {
                if (removedPic.Buffer != null)
                {
                    if (_pendingReuse.Remove(removedPic.Buffer))
                    {
                        _bufferToPicture.Remove(removedPic.Buffer);
                        removedPic.Buffer.V4L2Buffer.ResetPlanesUsed();
                        _availableCaptureBuffers.Add(removedPic.Buffer);
                    }
                    else
                    {
                        _bufferToPicture.Remove(removedPic.Buffer);
                    }
                }
            }

            // Calculate FrameNumWrap and PicNum
            picture.FrameNumWrap = (int)picture.FrameNum;
            picture.PicNum = picture.FieldPicFlag
                ? 2 * picture.FrameNumWrap + (picture.BottomFieldFlag ? 1 : 0)
                : picture.FrameNumWrap;

            _dpb.Add(picture);
            _bufferToPicture[buffer] = picture;

            _logger.LogTrace("Added ref picture to DPB: frame_num={FrameNum}, ref_ts={RefTs}, DPB size={Size}",
                picture.FrameNum, picture.ReferenceTs, _dpb.NumPics);
        }
    }

    private SharedDmaBuffer AcquireCaptureBuffer()
    {
        return _availableCaptureBuffers.Take();
    }

    // ============================================
    // Helpers
    // ============================================

    private static int GetStartCodeLength(ReadOnlySpan<byte> nalu)
    {
        if (nalu.Length >= 4 &&
            nalu[0] == 0x00 && nalu[1] == 0x00 && nalu[2] == 0x00 && nalu[3] == 0x01)
        {
            return 4;
        }

        if (nalu.Length >= 3 &&
            nalu[0] == 0x00 && nalu[1] == 0x00 && nalu[2] == 0x01)
        {
            return 3;
        }

        return 0;
    }

    private void Cleanup()
    {
        _logger.LogInformation("Cleaning up decoder resources...");

        StopStreaming();

        lock (_dpbLock)
        {
            _pendingReuse.Clear();
            _dpb.Clear();
            _bufferToPicture.Clear();
        }

        ResetBitstream();
        _availableCaptureBuffers.CompleteAdding();

        UnmapOutputBuffers();
        _device.Dispose();
        _mediaDevice.Dispose();

        _isInitialized = false;
        _logger.LogInformation("Decoder cleanup completed");
    }

    private void UnmapOutputBuffers()
    {
        foreach (var buffer in _device.OutputMPlaneQueue.BuffersPool.Buffers)
        {
            buffer.Unmap();
        }
    }
}

// ============================================
// V4L2 Buffer Flags (matching linux/v4l2-controls.h)
// ============================================
internal static class V4L2BufFlags
{
    public const uint M2M_HOLD_CAPTURE_BUF = 0x00000200;
}

// ============================================
// V4L2 Stateless H264 enums
// ============================================
internal enum V4L2StatelessH264DecodeMode
{
    SLICE_BASED = 0,
    FRAME_BASED = 1
}

internal enum V4L2StatelessH264StartCode
{
    NONE = 0,
    ANNEX_B = 1
}
