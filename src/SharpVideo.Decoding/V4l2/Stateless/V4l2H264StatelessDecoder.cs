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

    // Pending decode request tracking
    // Maps SystemFrameNumber to the buffer used for that frame
    // This is needed because V4L2 dequeue returns timestamp which identifies the frame
    private readonly Dictionary<uint, SharedDmaBuffer> _pendingDecodeRequests = new();
    private readonly object _pendingRequestsLock = new();

    // DPB and picture state
    private readonly H264Dpb _dpb;
    private readonly H264PicOrderCountCalculator _pocCalculator = new();
    private readonly H264ReferencePictureMarking _refPicMarking;
    private readonly H264FrameNumGapHandler _frameNumGapHandler;
    private readonly H264RefPicListBuilder _refPicListBuilder;
    private H264Picture? _currentPicture;
    private readonly Dictionary<SharedDmaBuffer, H264Picture> _bufferToPicture = new();
    // Tracks buffers that have been output for display but not yet returned by user
    // These buffers must NOT be reused even if their picture is no longer in DPB
    private readonly HashSet<SharedDmaBuffer> _inFlightDisplayBuffers = new();
    private readonly object _dpbLock = new();

    // Interlaced field handling (following GStreamer's last_field pattern)
    private H264Picture? _lastField;

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
    private int _maxPicNum;
    private int _maxFrameNum;

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

    // Reference picture lists (following GStreamer's ref_pic_list0/ref_pic_list1)
    // Built per-picture, modified per-slice via RPLM commands
    private List<H264Picture>? _refPicList0;
    private V4L2H264Reference[]? _refPicList0V4L2;

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

    // Intra refresh / recovery point support
    // Following GStreamer's H266 GDR pattern adapted for H264
    private bool _noOutputBeforeRecoveryFlag;
    private int _recoveryPointPoc = int.MinValue;
    private uint _pendingRecoveryFrameCnt;
    private bool _hasPendingRecoveryPoint;

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
        _refPicMarking = new H264ReferencePictureMarking(logger);
        _frameNumGapHandler = new H264FrameNumGapHandler(logger);
        _refPicListBuilder = new H264RefPicListBuilder(logger);

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

        _logger.LogTrace("Processing NALU type={NaluType} ({NaluTypeId}), size={Size}",
            naluState.nal_unit_header.NalUnitType,
            (int)naluState.nal_unit_header.NalUnitType,
            nalu.Length);

        switch (naluState.nal_unit_header.NalUnitType)
        {
            case NalUnitType.SPS_NUT:
                ProcessSps(naluState);
                break;

            case NalUnitType.PPS_NUT:
                ProcessPps(naluState);
                break;

            case NalUnitType.SEI_NUT:
                ProcessSei(naluState);
                break;

            case NalUnitType.CODED_SLICE_OF_NON_IDR_PICTURE_NUT:
            case NalUnitType.CODED_SLICE_OF_IDR_PICTURE_NUT:
                ProcessSlice(nalu, naluState, naluState.nal_unit_header.NalUnitType);
                break;

            case NalUnitType.AUD_NUT:
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
            // First, mark this buffer as no longer in-flight (returned from display)
            _inFlightDisplayBuffers.Remove(decodedFrame);

            if (_bufferToPicture.TryGetValue(decodedFrame, out var picture))
            {
                // Check if the picture is still in the DPB (either as reference or awaiting output)
                // A buffer can only be reused when the picture is completely removed from DPB
                bool stillInDpb = _dpb.GetPictures().Contains(picture);

                // Also check if OtherField (for interlaced) is still in DPB
                if (!stillInDpb && picture.OtherField != null)
                {
                    stillInDpb = _dpb.GetPictures().Contains(picture.OtherField);
                }

                if (stillInDpb)
                {
                    // Buffer is still in DPB, mark for pending reuse
                    _pendingReuse.Add(decodedFrame);
                    _logger.LogTrace("Buffer still in DPB (IsRef={IsRef}), deferring reuse", picture.IsRef);
                    return;
                }
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

        // Clear pending decode requests
        lock (_pendingRequestsLock)
        {
            _pendingDecodeRequests.Clear();
        }

        lock (_dpbLock)
        {
            // Drain all pictures in POC order before clearing
            DrainDpbOutput();

            // Return pending buffers that are not in-flight
            foreach (var buffer in _pendingReuse)
            {
                if (!_inFlightDisplayBuffers.Contains(buffer))
                {
                    buffer.V4L2Buffer.ResetPlanesUsed();
                    _availableCaptureBuffers.Add(buffer);
                }
            }
            _pendingReuse.Clear();
            // Note: in-flight buffers will be returned when user calls ReuseDecodedFrame

            foreach (var pic in _dpb.GetPictures())
            {
                if (pic.Buffer != null)
                {
                    _bufferToPicture.Remove(pic.Buffer);
                }
            }
            _dpb.Clear();
            _bufferToPicture.Clear();
            _lastField = null;
        }

        _currentPicture = null;
        _pocCalculator.Reset();
        _refPicListBuilder.Reset();

        // Reset intra refresh / recovery point state
        _recoveryPointPoc = int.MinValue;
        _noOutputBeforeRecoveryFlag = false;
        _hasPendingRecoveryPoint = false;
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

            // Get the frame number from the timestamp (following GStreamer's convention)
            // timestamp = tv_sec * 1_000_000 + tv_usec = SystemFrameNumber
            uint frameNumber = dequeuedBuffer.FrameNumber;

            // Look up the buffer that was assigned to this frame
            SharedDmaBuffer? decodedFrame;
            lock (_pendingRequestsLock)
            {
                if (!_pendingDecodeRequests.TryGetValue(frameNumber, out decodedFrame))
                {
                    _logger.LogWarning("Received decoded buffer for unknown frame number {FrameNumber}, using index-based lookup",
                        frameNumber);
                    // Fallback to index-based lookup (less reliable but maintains compatibility)
                    decodedFrame = _v4l2IndexToBuffer![dequeuedBuffer.Index];
                }
                else
                {
                    _pendingDecodeRequests.Remove(frameNumber);
                }
            }

            // Verify the buffer index matches (sanity check)
            if (decodedFrame.V4L2Buffer.Index != dequeuedBuffer.Index)
            {
                _logger.LogWarning("Buffer index mismatch for frame {FrameNumber}: expected {Expected}, got {Actual}",
                    frameNumber, decodedFrame.V4L2Buffer.Index, dequeuedBuffer.Index);
            }

            _logger.LogTrace("Decoded frame {FrameNumber} completed, buffer index {Index}",
                frameNumber, dequeuedBuffer.Index);

            // Mark buffer as in-flight (being displayed) before sending to output
            // This prevents it from being reused even if bumped from DPB
            lock (_dpbLock)
            {
                _inFlightDisplayBuffers.Add(decodedFrame);
            }

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

        // Update max_frame_num for reference list management
        _maxFrameNum = 1 << (int)(spsData.log2_max_frame_num_minus4 + 4);

        // Update DPB settings
        var maxRefs = (int)spsData.max_num_ref_frames;
        var maxAvailableRefs = (int)_configuration.CaptureBufferCount - 1;
        if (maxRefs > maxAvailableRefs)
        {
            _logger.LogWarning("max_num_ref_frames ({MaxRefs}) exceeds available buffers ({Available}), clamping",
                maxRefs, maxAvailableRefs);
            maxRefs = maxAvailableRefs;
        }
        _dpb.SetMaxNumRefFrames(maxRefs);
        _dpb.SetMaxNumPics(maxDpbSize);
        _dpb.Interlaced = interlaced;

        // Calculate max_num_reorder_frames (following GStreamer)
        int maxNumReorderFrames = GetMaxNumReorderFrames(spsData, maxDpbSize);
        _dpb.SetMaxNumReorderFrames(maxNumReorderFrames);

        if (negotiationNeeded)
        {
            StopStreaming();
            // Re-negotiate would go here if we supported dynamic resolution changes
        }
    }

    /// <summary>
    /// Calculate max_num_reorder_frames following GStreamer's logic.
    /// </summary>
    private static int GetMaxNumReorderFrames(SpsDataState sps, int maxDpbSize)
    {
        // If bitstream_restriction_flag is present, use max_num_reorder_frames
        if (sps.vui_parameters_present_flag != 0 &&
            sps.vui_parameters?.bitstream_restriction_flag != 0)
        {
            var numReorderFrames = (int)(sps.vui_parameters?.max_num_reorder_frames ?? 0);
            if (numReorderFrames > maxDpbSize)
            {
                return maxDpbSize;
            }
            return numReorderFrames;
        }

        // If constraint_set3_flag is set for specific profiles, infer 0
        if (sps.constraint_set3_flag != 0)
        {
            switch (sps.profile_idc)
            {
                case 44:
                case 86:
                case 100:
                case 110:
                case 122:
                case 244:
                    return 0;
            }
        }

        // Baseline profile has no B-frames
        if (sps.profile_idc == 66 || sps.profile_idc == 83)
        {
            return 0;
        }

        return maxDpbSize;
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

    private void ProcessSei(NalUnitState naluState)
    {
        var sei = naluState.nal_unit_payload.sei;
        if (sei == null)
        {
            _logger.LogTrace("SEI NALU received but parsing returned null");
            return;
        }

        _logger.LogDebug("SEI NALU parsed: {Count} messages", sei.Messages.Count);
        foreach (var msg in sei.Messages)
        {
            _logger.LogDebug("  SEI message: type={Type} ({TypeId}), size={Size}",
                msg.PayloadType, (int)msg.PayloadType, msg.PayloadSize);
        }

        // Process recovery point for intra refresh support
        var recoveryPoint = sei.RecoveryPoint;
        if (recoveryPoint != null)
        {
            _logger.LogInformation("SEI Recovery Point: recovery_frame_cnt={RecoveryFrameCnt}, exact_match={ExactMatch}, broken_link={BrokenLink}",
                recoveryPoint.RecoveryFrameCnt, recoveryPoint.ExactMatchFlag, recoveryPoint.BrokenLinkFlag);

            // Store the recovery point info to be applied when the next picture is created
            // The recovery_poc will be calculated as: current_poc + recovery_frame_cnt * poc_increment
            // where poc_increment = 2 for frame-only, or based on field structure
            _hasPendingRecoveryPoint = true;
            _pendingRecoveryFrameCnt = recoveryPoint.RecoveryFrameCnt;

            // If broken_link_flag is set, we should suppress output until recovery point is reached
            if (recoveryPoint.BrokenLinkFlag)
            {
                _noOutputBeforeRecoveryFlag = true;
            }
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

        // Find the first field of a complementary pair for interlaced content
        // Following GStreamer's gst_h264_decoder_find_first_field_picture
        H264Picture? firstField = FindFirstFieldPicture(sliceHeader);

        // Create picture (may be linked to first field)
        _currentPicture = CreatePictureForSlice(sliceHeader, sps, isIdr, firstField);
        FillDecodeParams(sliceHeader, _currentPicture);

        // Build initial reference list P0 (following GStreamer's gst_h264_decoder_prepare_ref_pic_lists)
        // This is done once per picture, then modified per-slice via RPLM in FillReferences
        lock (_dpbLock)
        {
            _refPicList0 = _refPicListBuilder.BuildRefPicListP0(_dpb);
        }

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

        // Get max_frame_num for gap handling and pic_num calculation
        int maxFrameNum = 1 << (int)(sps.sps_data.log2_max_frame_num_minus4 + 4);
        _maxPicNum = header.field_pic_flag != 0 ? 2 * maxFrameNum : maxFrameNum;

        // For IDR, clear DPB and reset POC (following GStreamer's gst_h264_decoder_process_slice_hdr)
        // Note: GStreamer checks no_output_of_prior_pics_flag to decide between drain vs clear.
        // For stateless V4L2 decoding, frames are output asynchronously via capture buffer loop,
        // so we just mark all as non-ref and clear the DPB state.
        if (isIdr)
        {
            lock (_dpbLock)
            {
                // Drain output before clearing (following GStreamer)
                DrainDpbOutput();

                // Return pending buffers that are not in-flight
                foreach (var buffer in _pendingReuse)
                {
                    if (!_inFlightDisplayBuffers.Contains(buffer))
                    {
                        buffer.V4L2Buffer.ResetPlanesUsed();
                        _availableCaptureBuffers.Add(buffer);
                    }
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
                _lastField = null;
            }
            _pocCalculator.Reset();
            _refPicMarking.Reset();
            _frameNumGapHandler.Reset();
            _refPicListBuilder.Reset();

            // IDR clears recovery state - we're at a clean point
            _recoveryPointPoc = int.MinValue;
            _noOutputBeforeRecoveryFlag = false;
            _hasPendingRecoveryPoint = false;
        }
        else
        {
            // Handle frame_num gaps (following GStreamer's gst_h264_decoder_handle_frame_num_gap)
            bool gapsAllowed = sps.sps_data.gaps_in_frame_num_value_allowed_flag != 0;
            lock (_dpbLock)
            {
                _frameNumGapHandler.HandleFrameNumGap(
                    _dpb,
                    header.frame_num,
                    maxFrameNum,
                    gapsAllowed,
                    isIdr,
                    CreateNonExistingPicture);
            }
        }

        // Calculate POC using the proper field-aware calculation (matching GStreamer)
        _pocCalculator.CalculatePOC(
            header,
            sps,
            isIdr,
            picture.Field,
            out int topPoc,
            out int bottomPoc);

        picture.TopFieldOrderCnt = topPoc;
        picture.BottomFieldOrderCnt = bottomPoc;

        // Store PicOrderCntMsb for UpdateAfterPicture (used by POC type 0)
        picture.PicOrderCntMsb = _pocCalculator.LastPicOrderCntMsb;

        // Handle intra refresh recovery point
        // Following GStreamer H266 GDR pattern adapted for H264
        if (_hasPendingRecoveryPoint)
        {
            _hasPendingRecoveryPoint = false;

            // Calculate recovery POC: current POC + recovery_frame_cnt * poc_increment
            // For frame-only content, poc_increment = 2 (per H.264 spec POC type 0/1/2 behavior)
            // For interlaced, it depends on field structure
            int pocIncrement = _interlaced ? 1 : 2;
            int currentPoc = picture.GetPicOrderCnt();
            _recoveryPointPoc = currentPoc + (int)_pendingRecoveryFrameCnt * pocIncrement;

            _logger.LogInformation("Recovery point set: current_poc={CurrentPoc}, recovery_frame_cnt={RecoveryFrameCnt}, recovery_poc={RecoveryPoc}",
                currentPoc, _pendingRecoveryFrameCnt, _recoveryPointPoc);
        }

        // Suppress output if before recovery point (following GStreamer H266 pattern)
        if (_noOutputBeforeRecoveryFlag && _recoveryPointPoc != int.MinValue)
        {
            int picPoc = picture.GetPicOrderCnt();
            if (picPoc < _recoveryPointPoc)
            {
                picture.OutputFlag = false;
                _logger.LogTrace("Suppressing output for picture POC={Poc} (recovery_poc={RecoveryPoc})",
                    picPoc, _recoveryPointPoc);
            }
            else
            {
                // Reached or passed recovery point - clear suppression
                _noOutputBeforeRecoveryFlag = false;
                _recoveryPointPoc = int.MinValue;
                _logger.LogInformation("Recovery point reached at POC={Poc}, resuming normal output", picPoc);
            }
        }

        // Update pic_nums for all pictures in DPB (following GStreamer's gst_h264_decoder_update_pic_nums)
        lock (_dpbLock)
        {
            _refPicListBuilder.UpdatePicNums(_dpb, picture, maxFrameNum);
        }

        // Calculate FrameNumWrap and PicNum for the current picture
        // This MUST be done before MMCO operations as they use PicNum for calculations
        // Following H.264 spec 8.2.4.1
        if (picture.FrameNum > _frameNumGapHandler.PrevRefFrameNum)
        {
            picture.FrameNumWrap = (int)picture.FrameNum;
        }
        else
        {
            picture.FrameNumWrap = (int)picture.FrameNum + maxFrameNum;
        }

        if (picture.Field == H264PictureField.Frame)
        {
            picture.PicNum = picture.FrameNumWrap;
        }
        else
        {
            picture.PicNum = 2 * picture.FrameNumWrap + 1;
        }

        _logger.LogDebug("Picture: sys_frame={SysFrame}, frame_num={FrameNum}, PicNum={PicNum}, TopPOC={TopPoc}, BottomPOC={BottomPoc}, IsRef={IsRef}",
            picture.SystemFrameNumber, picture.FrameNum, picture.PicNum, picture.TopFieldOrderCnt, picture.BottomFieldOrderCnt, picture.IsRef);

        return picture;
    }

    /// <summary>
    /// Drain pictures from DPB in POC order for output.
    /// Following GStreamer's _bump_dpb pattern.
    /// </summary>
    private void DrainDpbOutput()
    {
        while (_dpb.NeedsBump(null, H264DpbBumpMode.NormalLatency))
        {
            var toOutput = _dpb.Bump(true);
            if (toOutput == null)
            {
                break;
            }
            OutputPicture(toOutput);
        }
    }

    /// <summary>
    /// Perform DPB bumping if needed.
    /// Following GStreamer's _bump_dpb.
    /// </summary>
    private void BumpDpb(H264Picture? currentPicture, H264DpbBumpMode bumpMode)
    {
        while (_dpb.NeedsBump(currentPicture, bumpMode))
        {
            var toOutput = _dpb.Bump(false);
            if (toOutput == null)
            {
                _logger.LogWarning("Bumping is needed but no picture to output");
                break;
            }
            OutputPicture(toOutput);
        }
    }

    /// <summary>
    /// Output a picture that has been bumped from the DPB.
    /// For stateless V4L2, pictures are already being output via capture buffer processing,
    /// but this ensures proper POC ordering tracking.
    /// </summary>
    private void OutputPicture(H264Picture picture)
    {
        // Check if output should be suppressed (intra refresh before recovery point)
        if (!picture.OutputFlag)
        {
            _logger.LogTrace("Skipping output of picture due to OutputFlag=false: frame_num={FrameNum}, POC={Poc}",
                picture.FrameNum, picture.GetPicOrderCnt());
            // Note: Bump() already sets NeededForOutput = false and Outputted = true
            return;
        }

        _logger.LogTrace("Outputting picture: frame_num={FrameNum}, POC={Poc}",
            picture.FrameNum, picture.GetPicOrderCnt());
        // Note: Bump() already sets NeededForOutput = false and Outputted = true
    }

    /// <summary>
    /// Find the first field of a complementary field pair for interlaced content.
    /// Following GStreamer's gst_h264_decoder_find_first_field_picture.
    /// </summary>
    private H264Picture? FindFirstFieldPicture(SliceHeaderState sliceHeader)
    {
        if (!_interlaced)
        {
            return null;
        }

        // Not a field picture - no first field
        if (sliceHeader.field_pic_flag == 0)
        {
            // If there's a pending first field, it's incomplete
            if (_lastField != null)
            {
                _logger.LogWarning("Previous picture {Poc} is not complete (received frame)",
                    _lastField.GetPicOrderCnt());
                _lastField = null;
            }
            return null;
        }

        // Check if we have a cached first field
        if (_lastField != null)
        {
            // Verify it's a complementary pair
            if (_lastField.FrameNum == sliceHeader.frame_num)
            {
                H264PictureField currentField = sliceHeader.bottom_field_flag != 0
                    ? H264PictureField.BottomField
                    : H264PictureField.TopField;

                if (currentField != _lastField.Field)
                {
                    // Valid complementary pair
                    var firstField = _lastField;
                    _lastField = null;
                    return firstField;
                }
                else
                {
                    // Same field type - error
                    _logger.LogWarning("Current picture and previous picture have identical field {Field}",
                        currentField);
                    _lastField = null;
                    return null;
                }
            }
            else
            {
                // Different frame_num - first field was incomplete
                _logger.LogWarning("Previous picture {Poc} is not complete (different frame_num)",
                    _lastField.GetPicOrderCnt());
                _lastField = null;
            }
        }

        // Check DPB for an incomplete field picture (following GStreamer's DPB search)
        lock (_dpbLock)
        {
            var pics = _dpb.GetPictures();
            if (pics.Count > 0)
            {
                var lastPic = pics[^1];
                if (lastPic.Field != H264PictureField.Frame && lastPic.OtherField == null)
                {
                    if (lastPic.FrameNum == sliceHeader.frame_num)
                    {
                        H264PictureField currentField = sliceHeader.bottom_field_flag != 0
                            ? H264PictureField.BottomField
                            : H264PictureField.TopField;

                        if (currentField != lastPic.Field)
                        {
                            return lastPic;
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Create a picture for the current slice, linking to first field if this is second field.
    /// Following GStreamer's gst_h264_decoder_new_field_picture.
    /// </summary>
    private H264Picture CreatePictureForSlice(SliceHeaderState sliceHeader, SpsState sps, bool isIdr, H264Picture? firstField)
    {
        if (firstField != null)
        {
            // This is the second field - create a complementary field picture
            _systemFrameNumber++;
            var secondField = firstField.CreateComplementaryFieldPicture(_systemFrameNumber);
            secondField.InitFromSliceHeader(sliceHeader, sps, isIdr);

            // Calculate POC for the second field
            _pocCalculator.CalculatePOC(
                sliceHeader,
                sps,
                isIdr,
                secondField.Field,
                out int topPoc,
                out int bottomPoc);

            secondField.TopFieldOrderCnt = topPoc;
            secondField.BottomFieldOrderCnt = bottomPoc;

            _logger.LogDebug("Second field: sys_frame={SysFrame}, frame_num={FrameNum}, POC={Poc}",
                secondField.SystemFrameNumber, secondField.FrameNum, secondField.GetPicOrderCnt());

            return secondField;
        }

        // Regular picture (first field or frame)
        var picture = CreatePicture(sliceHeader, sps, isIdr);

        // If this is a field picture and not linked to another, cache it as last_field
        if (picture.Field != H264PictureField.Frame && picture.OtherField == null)
        {
            _lastField = picture;
        }

        return picture;
    }

    /// <summary>
    /// Creates a non-existing picture for frame_num gap filling.
    /// </summary>
    private H264Picture? CreateNonExistingPicture(uint frameNum)
    {
        _systemFrameNumber++;

        return new H264Picture
        {
            SystemFrameNumber = _systemFrameNumber,
            FrameNum = frameNum,
            IsNonExisting = true,
            IsRef = true,
            IsLongTermRef = false,
            Field = H264PictureField.Frame
        };
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

        // Set field order counts based on picture field (matching GStreamer exactly)
        // GStreamer uses the calculated POC values from the picture struct, not raw slice header values
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

        // Handle field picture boundary detection for interlaced content
        // Following GStreamer's field picture boundary check in gst_h264_decoder_parse_slice
        if (_interlaced && _currentPicture != null &&
            _currentPicture.Field != H264PictureField.Frame &&
            !_currentPicture.SecondField)
        {
            H264PictureField curField = header.field_pic_flag != 0
                ? (header.bottom_field_flag != 0 ? H264PictureField.BottomField : H264PictureField.TopField)
                : H264PictureField.Frame;

            if (curField != _currentPicture.Field)
            {
                _logger.LogTrace("Found new field picture, finishing the first field picture");
                EndPicture();
            }
        }

        // Check if this is the first slice of a new picture
        if (header.first_mb_in_slice == 0)
        {
            // End previous picture if any
            EndPicture();

            // Start new picture (may be second field of a pair)
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
        bool isFrame = _currentPicture?.Field == H264PictureField.Frame;
        var sliceParams = SliceParamsMapper.BuildSliceParams(
            header, _currentPps!, _decodeParams.Dpb, _refPicList0V4L2, isFrame);
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
        // Following gst_v4l2_codec_h264_dec_fill_references
        // Apply Reference Picture List Modification (RPLM) commands from slice header
        // Then convert to V4L2 DPB indices

        if (_currentPicture == null || _refPicList0 == null)
        {
            _refPicList0V4L2 = null;
            return;
        }

        uint sliceType = header.slice_type % 5;

        // B-slices not supported
        if (sliceType == 1)
        {
            throw new NotImplementedException("B-frames are not supported");
        }

        // For I-slices, no reference list needed
        if (sliceType == 2 || sliceType == 4) // I or SI slice
        {
            _refPicList0V4L2 = null;
            return;
        }

        // Apply RPLM for P/SP slices (sliceType 0 or 3)
        List<H264Picture> modifiedList;
        lock (_dpbLock)
        {
            var (refPicList0, _) = _refPicListBuilder.ModifyRefPicLists(
                _dpb, _currentPicture, header, _maxPicNum);
            modifiedList = refPicList0;
        }

        // Validate num_ref_idx_l0_active_minus1
        int numRefIdxL0Active = (int)header.num_ref_idx_l0_active_minus1 + 1;
        if (numRefIdxL0Active > modifiedList.Count)
        {
            _logger.LogWarning(
                "num_ref_idx_l0_active ({Active}) exceeds available references ({Available}), clamping",
                numRefIdxL0Active, modifiedList.Count);
            numRefIdxL0Active = modifiedList.Count;
        }

        // Convert to V4L2 reference list format
        bool isFrame = _currentPicture.Field == H264PictureField.Frame;
        _refPicList0V4L2 = new V4L2H264Reference[V4L2H264Constants.V4L2_H264_REF_LIST_LEN];

        // Initialize all entries to invalid
        for (int i = 0; i < _refPicList0V4L2.Length; i++)
        {
            _refPicList0V4L2[i].Index = 0xff;
            _refPicList0V4L2[i].Fields = 0;
        }

        // Fill with valid references
        for (int i = 0; i < numRefIdxL0Active && i < modifiedList.Count; i++)
        {
            var refPic = modifiedList[i];
            byte dpbIndex = H264Dpb.LookupDpbIndex(_decodeParams.Dpb, refPic);

            _refPicList0V4L2[i].Index = dpbIndex;
            _refPicList0V4L2[i].Fields = GetV4L2FieldsRef(refPic, isFrame);

            if (dpbIndex == 0xff)
            {
                _logger.LogWarning(
                    "Reference picture not found in DPB: frame_num={FrameNum}, PicNum={PicNum}",
                    refPic.FrameNum, refPic.PicNum);
            }
        }

        // Diagnostic logging for L0 list
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            LogRefPicList0(modifiedList, numRefIdxL0Active);
        }
    }

    /// <summary>
    /// Get V4L2 fields reference flags for a reference picture.
    /// Following GStreamer's _get_v4l2_fields_ref.
    /// </summary>
    private static byte GetV4L2FieldsRef(H264Picture refPic, bool merge)
    {
        if (merge && refPic.OtherField != null)
        {
            return V4L2H264Constants.V4L2_H264_FRAME_REF;
        }

        return refPic.Field switch
        {
            H264PictureField.Frame => V4L2H264Constants.V4L2_H264_FRAME_REF,
            H264PictureField.TopField => V4L2H264Constants.V4L2_H264_TOP_FIELD_REF,
            H264PictureField.BottomField => V4L2H264Constants.V4L2_H264_BOTTOM_FIELD_REF,
            _ => V4L2H264Constants.V4L2_H264_FRAME_REF
        };
    }

    /// <summary>
    /// Log reference picture list L0 for debugging.
    /// </summary>
    private void LogRefPicList0(List<H264Picture> refPicList0, int activeCount)
    {
        _logger.LogDebug("RefPicList0 ({Count} active):", activeCount);
        for (int i = 0; i < activeCount && i < refPicList0.Count; i++)
        {
            var pic = refPicList0[i];
            byte dpbIdx = _refPicList0V4L2 != null ? _refPicList0V4L2[i].Index : (byte)0xff;
            _logger.LogDebug(
                "  [{Index}] FrameNum={FrameNum}, PicNum={PicNum}, POC={Poc}, IsLongTerm={IsLT}, DpbIdx={DpbIdx}",
                i, pic.FrameNum, pic.PicNum, pic.GetPicOrderCnt(), pic.IsLongTermRef, dpbIdx);
        }
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
        _refPicList0 = null;
        _refPicList0V4L2 = null;
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

        // Timestamp for OUTPUT buffer (following GStreamer gstv4l2decoder.c)
        // The reference_ts in DPB entries = SystemFrameNumber * 1000 (nanoseconds)
        // V4L2 buffer timestamp: tv_sec * 1_000_000_000 + tv_usec * 1000 = nanoseconds
        // So we set: tv_sec = frame_num / 1_000_000, tv_usec = frame_num % 1_000_000
        // This gives: timestamp_ns = frame_num * 1000 = reference_ts
        var timestamp = new TimeVal
        {
            TvSec = (nint)(_currentPicture.SystemFrameNumber / 1_000_000),
            TvUsec = (nint)(_currentPicture.SystemFrameNumber % 1_000_000)
        };

        // Register the pending decode request before submitting
        // This maps the frame number to the buffer so we can identify it when dequeued
        lock (_pendingRequestsLock)
        {
            _pendingDecodeRequests[_currentPicture.SystemFrameNumber] = captureBuffer;
        }

        // Write bitstream and queue OUTPUT buffer FIRST with timestamp (like GStreamer)
        var bitstreamData = _bitstreamBuffer.ToArray();
        _device.OutputMPlaneQueue.WriteBufferAndEnqueue(bitstreamData, request, timestamp);

        // Queue CAPTURE buffer WITHOUT timestamp (like GStreamer: gst_v4l2_decoder_queue_src_buffer)
        // GStreamer does NOT set timestamp on capture buffer
        _device.CaptureMPlaneQueue.EnqueueDmaBufBuffer(captureBuffer.V4L2Buffer, request, null);

        // Queue the media request
        request.Queue();

        StartStreaming();

        // Process the picture - add to DPB, do bumping, etc.
        // This must be done for ALL pictures, not just reference ones
        FinishPicture(_currentPicture, captureBuffer);

        _sliceParams.Clear();
    }

    /// <summary>
    /// Finish processing a picture after it has been submitted for decoding.
    /// Following GStreamer's gst_h264_decoder_finish_picture.
    /// This handles reference picture marking, DPB management, and output ordering.
    /// </summary>
    private void FinishPicture(H264Picture picture, SharedDmaBuffer buffer)
    {
        lock (_dpbLock)
        {
            // For reference pictures, perform marking BEFORE bumping
            if (picture.IsRef)
            {
                // Perform reference picture marking (following GStreamer's gst_h264_decoder_reference_picture_marking)
                // This handles both adaptive ref pic marking (MMCO) and sliding window
                _refPicMarking.PerformMarking(_dpb, picture, _currentSliceHeader?.dec_ref_pic_marking);

                // Update prev state after marking (for MMCO 5 handling)
                _pocCalculator.UpdateAfterPicture(picture);
                _frameNumGapHandler.UpdatePrevRefFrameNum(picture.FrameNum);
            }

            // Remove unused pictures before bumping
            var removedPictures = _dpb.RemoveUnusedPictures();
            ReturnRemovedPicturesToPool(removedPictures);

            // C.4.4: if mem_mgmt_5, drain the DPB first
            if (picture.MemMgmt5)
            {
                _logger.LogTrace("Memory management type 5, draining the DPB");
                DrainDpbOutput();
            }

            // DPB Bumping - output pictures in POC order if needed (following GStreamer's _bump_dpb)
            BumpDpb(picture, H264DpbBumpMode.NormalLatency);

            // Note: PicNum is already calculated in CreatePicture before MMCO operations

            // C.4.5.1, C.4.5.2:
            // - If the current decoded picture is the second field of a complementary
            //   reference field pair, add to DPB.
            // C.4.5.1: For a reference decoded picture, the "bumping" process is invoked
            //   repeatedly until there is an empty frame buffer, then add to DPB.
            // C.4.5.2: For a non-reference decoded picture, if there is empty frame buffer
            //   after bumping the smaller POC, add to DPB. Otherwise, output directly.
            bool shouldAddToDpb =
                (picture.SecondField && picture.OtherField != null && picture.OtherField.IsRef) ||
                picture.IsRef ||
                _dpb.HasEmptyFrameBuffer();

            if (shouldAddToDpb)
            {
                // Handle interlaced: if the first field of last_field was cached,
                // add it to DPB when its second field arrives
                // Following GStreamer's add_picture_to_dpb
                if (_interlaced && picture.SecondField && picture.OtherField != null)
                {
                    // Check if first field is already in DPB
                    if (!_dpb.GetPictures().Contains(picture.OtherField))
                    {
                        _dpb.Add(picture.OtherField);
                        if (picture.OtherField.Buffer != null)
                        {
                            _bufferToPicture[picture.OtherField.Buffer] = picture.OtherField;
                        }
                    }
                }

                // For interlaced frame pictures, split into fields for proper reference marking
                // Following GStreamer's frame splitting logic in gst_h264_decoder_finish_picture
                if (_interlaced && picture.Field == H264PictureField.Frame)
                {
                    var otherField = picture.SplitFrame(_systemFrameNumber++);
                    if (otherField != null)
                    {
                        _dpb.Add(otherField);
                        // Note: other field shares the same buffer
                    }
                }

                _dpb.Add(picture);
                _bufferToPicture[buffer] = picture;

                _logger.LogTrace("Added picture to DPB: frame_num={FrameNum}, POC={Poc}, IsRef={IsRef}, DPB size={Size}",
                    picture.FrameNum, picture.GetPicOrderCnt(), picture.IsRef, _dpb.NumPics);
            }
            else
            {
                // No space in DPB for non-reference picture, output directly
                _bufferToPicture[buffer] = picture;
                OutputPictureDirectly(picture);
                _logger.LogTrace("Output non-ref picture directly (no DPB space): frame_num={FrameNum}, POC={Poc}",
                    picture.FrameNum, picture.GetPicOrderCnt());
            }
        }
    }

    /// <summary>
    /// Output a picture directly without going through DPB.
    /// Following GStreamer's output_picture_directly.
    /// </summary>
    private void OutputPictureDirectly(H264Picture picture)
    {
        // Check if output should be suppressed
        if (!picture.OutputFlag)
        {
            _logger.LogTrace("Skipping direct output due to OutputFlag=false: frame_num={FrameNum}, POC={Poc}",
                picture.FrameNum, picture.GetPicOrderCnt());
            picture.Outputted = true;
            picture.NeededForOutput = false;
            return;
        }

        picture.Outputted = true;
        picture.NeededForOutput = false;

        _logger.LogTrace("Direct output picture: frame_num={FrameNum}, POC={Poc}",
            picture.FrameNum, picture.GetPicOrderCnt());
    }

    /// <summary>
    /// Return removed pictures' buffers to the pool if they were pending reuse.
    /// </summary>
    private void ReturnRemovedPicturesToPool(List<H264Picture> removedPictures)
    {
        foreach (var removedPic in removedPictures)
        {
            if (removedPic.Buffer != null)
            {
                // Only return buffer to pool if:
                // 1. User called ReuseDecodedFrame but we deferred it (pendingReuse)
                // 2. Buffer is NOT currently being displayed (not in-flight)
                if (_pendingReuse.Remove(removedPic.Buffer))
                {
                    // Check if buffer is still being displayed
                    if (_inFlightDisplayBuffers.Contains(removedPic.Buffer))
                    {
                        // Buffer is still on screen, it will be returned when user calls ReuseDecodedFrame
                        _logger.LogTrace("Buffer pending but still in-flight (on display): frame_num={FrameNum}",
                            removedPic.FrameNum);
                        continue;
                    }

                    _bufferToPicture.Remove(removedPic.Buffer);
                    removedPic.Buffer.V4L2Buffer.ResetPlanesUsed();
                    _availableCaptureBuffers.Add(removedPic.Buffer);

                    _logger.LogTrace("Returned pending buffer to pool after DPB removal: frame_num={FrameNum}",
                        removedPic.FrameNum);
                }
            }
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

        lock (_pendingRequestsLock)
        {
            _pendingDecodeRequests.Clear();
        }

        lock (_dpbLock)
        {
            _pendingReuse.Clear();
            _inFlightDisplayBuffers.Clear();
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
