using System.Collections.Concurrent;
using System.IO;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;
using System.Linq;

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
/// DPB management follows GStreamer's gstv4l2codech264dec.c implementation.
/// </summary>
[SupportedOSPlatform("linux")]
public class V4l2H264StatelessDecoder : BaseDecoder<SharedDmaBuffer>
{
    private readonly V4L2Device _device;
    private readonly MediaDevice _mediaDevice;

    private readonly ILogger<V4l2H264StatelessDecoder> _logger;
    private readonly V4l2DecoderConfiguration _configuration;
    private readonly DrmBufferManager _drmBufferManager;

    private List<SharedDmaBuffer>? _drmBuffers;
    // Map from V4L2 buffer index to SharedDmaBuffer for fast lookup during dequeue
    private Dictionary<uint, SharedDmaBuffer>? _v4l2IndexToBuffer;

    private bool _supportsSliceParamsControl;
    private bool _supportsScalingMatrixControl;

    private readonly BlockingCollection<SharedDmaBuffer> _availableCaptureBuffers = new();
    private readonly HashSet<SharedDmaBuffer> _pendingReuse = new();
    private readonly object _dpbLock = new();

    // Pending frame assembly for multi-slice frames in frame-based mode
    private MemoryStream? _pendingFrameData;
    private SliceHeaderState? _pendingSliceHeader;
    private PpsState? _pendingPps;
    private SpsState? _pendingSps;
    private bool _pendingIsKeyFrame;
    // Accumulated slice types for multi-slice frames (matching GStreamer's cumulative flags)
    private HashSet<uint>? _pendingSliceTypes;

    // Thread for capture buffer processing
    private Thread? _captureThread;
    private CancellationTokenSource? _captureCts;

    // DPB (Decoded Picture Buffer) - following GStreamer's model
    private readonly H264Dpb _dpb;

    // Current picture being decoded
    private H264Picture? _currentPicture;

    // Map from buffer to picture for buffer lifecycle management
    private readonly Dictionary<SharedDmaBuffer, H264Picture> _bufferToPicture = new();

    private readonly H264PicOrderCountCalculator _pocCalculator = new();

    // System frame counter for generating unique timestamps (matches GStreamer's system_frame_number)
    private uint _systemFrameNumber;

    // H264 bitstream parsing state
    private readonly H264BitstreamParserState _streamState = new();
    private readonly ParsingOptions _parsingOptions = new() { add_checksum = false };

    private bool _isInitialized;
    private bool _streamingStarted;

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
        if (!_isInitialized)
        {
            InitializeDecoder();
        }
    }

    /// <inheritdoc />
    public override void Decode(ReadOnlySpan<byte> nalu)
    {
        if (_device == null)
        {
            throw new InvalidOperationException("Decoder not initialized. Call Initialize() first.");
        }

        if (nalu.Length < 4)
        {
            return;
        }

        // Determine start code length and get NALU type from first byte after start code
        int startCodeLength = GetStartCodeLength(nalu);
        if (startCodeLength == 0 || nalu.Length <= startCodeLength)
        {
            return;
        }

        // Parse the NALU to update stream state (SPS/PPS) or get slice header
        var naluState = H264NalUnitParser.ParseNalUnit(nalu.Slice(startCodeLength), _streamState, _parsingOptions);
        if (naluState == null)
        {
            _logger.LogWarning("Failed to parse NALU");
            return;
        }

        // Process based on NALU type - only copy to V4L2 buffer for slice data
        switch (naluState.nal_unit_header.NalUnitType)
        {
            case NalUnitType.SPS_NUT:
                ProcessSpsNalu(naluState);
                break;

            case NalUnitType.PPS_NUT:
                ProcessPpsNalu(naluState);
                break;

            case NalUnitType.CODED_SLICE_OF_NON_IDR_PICTURE_NUT:
            case NalUnitType.CODED_SLICE_OF_IDR_PICTURE_NUT:
                ProcessSliceNalu(nalu, naluState, naluState.nal_unit_header.NalUnitType);
                break;

            default:
                _logger.LogTrace("Skipping NALU type {NaluType}", naluState.nal_unit_header.NalUnitType);
                break;
        }
    }

    private static int GetStartCodeLength(ReadOnlySpan<byte> nalu)
    {
        // Check for 4-byte start code: 0x00 0x00 0x00 0x01
        if (nalu.Length >= 4 &&
            nalu[0] == 0x00 && nalu[1] == 0x00 && nalu[2] == 0x00 && nalu[3] == 0x01)
        {
            return 4;
        }

        // Check for 3-byte start code: 0x00 0x00 0x01
        if (nalu.Length >= 3 &&
            nalu[0] == 0x00 && nalu[1] == 0x00 && nalu[2] == 0x01)
        {
            return 3;
        }

        return 0;
    }

    private void ProcessSpsNalu(NalUnitState naluState)
    {
        var spsData = naluState.nal_unit_payload.sps?.sps_data;
        if (spsData != null)
        {
            _logger.LogInformation(
                "SPS RECEIVED: id={SpsId}, profile={Profile}, level={Level}, size={Width}x{Height}, max_num_ref_frames={MaxRefs}",
                spsData.seq_parameter_set_id,
                spsData.profile_idc,
                spsData.level_idc,
                (spsData.pic_width_in_mbs_minus1 + 1) * 16,
                (spsData.pic_height_in_map_units_minus1 + 1) * 16,
                spsData.max_num_ref_frames);

            // Update DPB configuration based on SPS
            // Clamp to available capture buffers minus 1 (for current frame being decoded)
            var maxRefs = (int)spsData.max_num_ref_frames;
            var maxAvailableRefs = (int)_configuration.CaptureBufferCount - 1;
            if (maxRefs > maxAvailableRefs)
            {
                _logger.LogWarning(
                    "SPS max_num_ref_frames ({MaxRefs}) exceeds available capture buffers ({Available}), clamping to {Clamped}",
                    maxRefs, _configuration.CaptureBufferCount, maxAvailableRefs);
                maxRefs = maxAvailableRefs;
            }
            _dpb.SetMaxNumRefFrames(maxRefs);
        }
        // SPS is stored in _streamState by the parser, no V4L2 buffer needed
    }

    private void ProcessPpsNalu(NalUnitState naluState)
    {
        var ppsData = naluState.nal_unit_payload.pps;
        if (ppsData != null)
        {
            _logger.LogInformation(
                "PPS RECEIVED: id={PpsId}, references SPS={SpsId}",
                ppsData.pic_parameter_set_id,
                ppsData.seq_parameter_set_id);
        }
        // PPS is stored in _streamState by the parser, no V4L2 buffer needed
    }

    private void ProcessSliceNalu(ReadOnlySpan<byte> nalu, NalUnitState naluState, NalUnitType naluType)
    {
        _logger.LogTrace("Processing slice NALU type {NaluType}", naluType);

        var sliceData = naluState.nal_unit_payload.slice_layer_without_partitioning_rbsp;
        if (sliceData == null)
        {
            _logger.LogWarning("Failed to parse slice data for NALU type {NaluType}, skipping", naluType);
            return;
        }

        var header = sliceData.slice_header;

        // Check if PPS/SPS are available
        if (!_streamState.pps.TryGetValue(header.pic_parameter_set_id, out var pps) || pps == null)
        {
            _logger.LogWarning("Cannot decode frame: PPS {PpsId} not received yet, skipping frame {FrameNum}",
                header.pic_parameter_set_id, header.frame_num);
            return;
        }

        if (!_streamState.sps.TryGetValue(pps.seq_parameter_set_id, out var sps) || sps == null)
        {
            _logger.LogWarning("Cannot decode frame: SPS {SpsId} not received yet, skipping frame {FrameNum}",
                pps.seq_parameter_set_id, header.frame_num);
            return;
        }

        bool isKeyFrame = naluType == NalUnitType.CODED_SLICE_OF_IDR_PICTURE_NUT;

        if (header.first_mb_in_slice == 0)
        {
            // New access unit: flush any pending assembled frame first
            SubmitPendingFrameIfAny();
            StartPendingFrame(nalu, header, isKeyFrame, pps, sps);
        }
        else
        {
            if (_pendingFrameData == null)
            {
                _logger.LogWarning("Received non-initial slice without an active frame assembly; dropping slice for frame {FrameNum}", header.frame_num);
                return;
            }

            // Accumulate slice type for multi-slice frames (following GStreamer's |= approach)
            _pendingSliceTypes?.Add(header.slice_type % 5);
            _pendingFrameData.Write(nalu);
        }
    }

    private void StartPendingFrame(ReadOnlySpan<byte> nalu, SliceHeaderState header, bool isKeyFrame, PpsState pps, SpsState sps)
    {
        _pendingFrameData = new MemoryStream();
        _pendingFrameData.Write(nalu);

        _pendingSliceHeader = header;
        _pendingPps = pps;
        _pendingSps = sps;
        _pendingIsKeyFrame = isKeyFrame;
        // Initialize slice types with the first slice type
        _pendingSliceTypes = new HashSet<uint> { header.slice_type % 5 };

        _logger.LogDebug("Started assembling frame: frame_num={FrameNum}, PPS={PpsId}, SPS={SpsId}, KeyFrame={IsKeyFrame}",
            header.frame_num, header.pic_parameter_set_id, pps.seq_parameter_set_id, isKeyFrame);
    }

    private void SubmitPendingFrameIfAny()
    {
        if (_pendingFrameData == null || _pendingSliceHeader == null || _pendingPps == null || _pendingSps == null)
        {
            return;
        }

        try
        {
            var assembled = _pendingFrameData.ToArray();
            var sliceTypes = _pendingSliceTypes ?? new HashSet<uint> { _pendingSliceHeader.slice_type % 5 };
            SubmitFrameToDevice(assembled, _pendingSliceHeader, _pendingIsKeyFrame, _pendingPps, _pendingSps, sliceTypes);
        }
        finally
        {
            ResetPendingFrame();
        }
    }

    private void ResetPendingFrame()
    {
        _pendingFrameData?.Dispose();
        _pendingFrameData = null;
        _pendingSliceHeader = null;
        _pendingPps = null;
        _pendingSps = null;
        _pendingIsKeyFrame = false;
        _pendingSliceTypes = null;
    }

    /// <inheritdoc />
    public override void ReuseDecodedFrame(SharedDmaBuffer decodedFrame)
    {
        if (_device == null)
        {
            throw new InvalidOperationException("Decoder not initialized");
        }

        lock (_dpbLock)
        {
            // Check if the buffer is still referenced by a picture in DPB
            if (_bufferToPicture.TryGetValue(decodedFrame, out var picture) && picture.IsRef)
            {
                // Buffer is still used as reference, mark for pending reuse
                // It will be returned when the picture is marked as non-ref
                _pendingReuse.Add(decodedFrame);
                _logger.LogTrace("Buffer still referenced in DPB, deferring reuse");
                return;
            }

            // Remove from pending reuse if it was there
            _pendingReuse.Remove(decodedFrame);
            // Remove from picture mapping if present
            _bufferToPicture.Remove(decodedFrame);
        }

        decodedFrame.V4L2Buffer.ResetPlanesUsed();
        _availableCaptureBuffers.Add(decodedFrame);
    }

    /// <inheritdoc />
    protected override void FlushDecoder()
    {
        _logger.LogInformation("Flushing decoder...");
        SubmitPendingFrameIfAny();
        ResetPendingFrame();

        lock (_dpbLock)
        {
            // Return all pending reuse buffers to the pool
            foreach (var buffer in _pendingReuse)
            {
                buffer.V4L2Buffer.ResetPlanesUsed();
                _availableCaptureBuffers.Add(buffer);
            }
            _pendingReuse.Clear();

            // Clear DPB and release all buffers
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

    private void InitializeDecoder()
    {
        _logger.LogInformation("Initializing H.264 stateless decoder...");

        // Log device information for debugging
        _logger.LogInformation("Device fd: {Fd}, Controls: {ControlCount}, ExtControls: {ExtControlCount}",
            _device.fd, _device.Controls.Count, _device.ExtendedControls.Count);

        // Configure decoder formats
        ConfigureFormats();

        DetectControlSupport();

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

        // Verify streaming is actually working
        var outputFormat = _device.GetOutputFormatMPlane();
        var captureFormat = _device.GetCaptureFormatMPlane();

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
            PixelFormat = OutputPixelFormat.Fourcc, // Usually NV12
            NumPlanes = 2, // NV12 typically has 2 planes
            Field = (uint)V4L2Field.NONE,
            Colorspace = 5,
            YcbcrEncoding = 1,
            Quantization = 1,
            XferFunc = 1
        };

        _device.SetCaptureFormatMPlane(captureFormat);
    }

    private void DetectControlSupport()
    {
        _supportsSliceParamsControl = _device.ExtendedControls.Any(
            c => c.Id == V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SLICE_PARAMS);
        var hasDecodeParams = _device.ExtendedControls.Any(
            c => c.Id == V4l2ControlsConstants.V4L2_CID_STATELESS_H264_DECODE_PARAMS);

        _supportsScalingMatrixControl = _device.ExtendedControls.Any(
            c => c.Id == V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SCALING_MATRIX);

        if (!_supportsSliceParamsControl)
        {
            _logger.LogWarning("Device does not report SLICE_PARAMS control support; decoder will run without it");
        }
        if (!_supportsScalingMatrixControl)
        {
            _logger.LogWarning("Device does not report SCALING_MATRIX control support; decoder will run without it");
        }
    }


    private void SetupAndMapBuffers()
    {
        _logger.LogInformation("Setting up and mapping buffers...");

        // Setup OUTPUT buffers for encoded data (MMAP)
        SetupOutputMMapBuffers();

        // Setup CAPTURE buffers for decoded frames (DMA-BUF)
        SetupDmaBufCaptureQueue();
    }

    private void SetupOutputMMapBuffers()
    {
        _device!.OutputMPlaneQueue.InitMMap(_configuration.OutputBufferCount);
        foreach (var buffer in _device.OutputMPlaneQueue.BuffersPool.Buffers)
        {
            buffer.MapToMemory();
        }

        if (_mediaDevice != null)
        {
            _mediaDevice.AllocateMediaRequests(_configuration.RequestPoolSize);
            _device.OutputMPlaneQueue.AssociateMediaRequests(_mediaDevice.OpenedRequests);
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

        // Build index mapping for fast dequeue lookup
        _v4l2IndexToBuffer = new Dictionary<uint, SharedDmaBuffer>();

        foreach (var buffer in _drmBuffers)
        {
            buffer.V4L2Buffer = _device.CaptureMPlaneQueue.DmaBufBuffersPool.Buffers
                .Single(b => b.DmaBufferFd == buffer.DmaBuffer.Fd);

            // Map V4L2 buffer index to SharedDmaBuffer
            _v4l2IndexToBuffer[buffer.V4L2Buffer.Index] = buffer;

            // Track availability for DPB management
            buffer.V4L2Buffer.ResetPlanesUsed();
            _availableCaptureBuffers.Add(buffer);
        }
    }

    private void EnsureStreamingStarted()
    {
        if (_streamingStarted)
        {
            return;
        }

        _logger.LogInformation("Starting V4L2 streaming...");

        _device!.OutputMPlaneQueue.StreamOn();
        _device.CaptureMPlaneQueue.StreamOn();

        _captureCts = new CancellationTokenSource();
        _captureThread = new Thread(ProcessCaptureBuffersThreadProc)
        {
            Name = "V4L2CaptureBufferProcessor", IsBackground = true
        };
        _captureThread.Start();

        _streamingStarted = true;
        _logger.LogInformation("Started capture buffer processing thread");
    }

    private void ProcessCaptureBuffersThreadProc()
    {
        var cancellationToken = _captureCts!.Token;
        _logger.LogInformation("Capture buffer processing thread started");

        while (!cancellationToken.IsCancellationRequested)
        {
            // Reduced timeout for lower latency (was 1000ms, now 16ms ~= 1 frame at 60fps)
            var dequeuedBuffer = _device!.CaptureMPlaneQueue.WaitForReadyBuffer(16);
            if (dequeuedBuffer == null)
            {
                continue;
            }

            // Find the SharedDmaBuffer by V4L2 buffer index
            var decodedFrame = _v4l2IndexToBuffer![dequeuedBuffer.Index];
            AddDecodedFrameToOutput(decodedFrame);
        }

        _logger.LogInformation("Capture buffer processing thread stopped");
    }

    private SharedDmaBuffer AcquireCaptureBuffer()
    {
        // Blocks until a free capture buffer is available (not referenced in DPB and returned by presenter)
        return _availableCaptureBuffers.Take();
    }

    private void SubmitFrameToDevice(
        ReadOnlySpan<byte> frameData,
        SliceHeaderState header,
        bool isKeyFrame,
        PpsState pps,
        SpsState sps,
        HashSet<uint> accumulatedSliceTypes)
    {
        // Increment system frame number for unique picture identification
        // Following GStreamer convention: system_frame_number is used to generate reference_ts
        _systemFrameNumber++;

        // Create a new H264Picture for this frame (following GStreamer's model)
        var picture = new H264Picture
        {
            SystemFrameNumber = _systemFrameNumber
        };
        picture.InitFromSliceHeader(header, sps, isKeyFrame);

        // For IDR pictures, clear the DPB and reset POC calculator
        if (isKeyFrame)
        {
            lock (_dpbLock)
            {
                // Return all pending reuse buffers to the pool since they're no longer references
                foreach (var buffer in _pendingReuse)
                {
                    buffer.V4L2Buffer.ResetPlanesUsed();
                    _availableCaptureBuffers.Add(buffer);
                    _logger.LogTrace("Released pending reuse buffer on IDR");
                }
                _pendingReuse.Clear();

                // Mark all current references as non-ref before clearing
                _dpb.MarkAllNonRef();

                // Release buffers from pictures that are no longer needed
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

        // Calculate POC for this picture
        var topPoc = _pocCalculator.CalculatePOC(header, sps, isKeyFrame);
        picture.TopFieldOrderCnt = topPoc;
        picture.BottomFieldOrderCnt = topPoc + header.delta_pic_order_cnt_bottom;

        _logger.LogDebug("Picture: system_frame={SysFrame}, frame_num={FrameNum}, POC={Poc}, IsRef={IsRef}",
            picture.SystemFrameNumber, picture.FrameNum, picture.GetPicOrderCnt(), picture.IsRef);

        // First, ensure there's a free OUTPUT buffer available before acquiring media request
        _device!.OutputMPlaneQueue.EnsureFreeBuffer();

        // Acquire a capture buffer that is safe to reuse
        var captureBuffer = AcquireCaptureBuffer();
        captureBuffer.V4L2Buffer.ResetPlanesUsed();

        // Associate buffer with picture
        picture.Buffer = captureBuffer;

        // Get DPB snapshot for decode params BEFORE adding current picture
        V4L2H264DpbEntry[] dpbSnapshot;
        lock (_dpbLock)
        {
            dpbSnapshot = _dpb.CreateV4L2Dpb();
        }

        // Timestamp for OUTPUT buffer QBUF: split system_frame_number into seconds and microseconds
        // The driver will copy this timestamp to the CAPTURE buffer automatically
        // Following GStreamer: use system_frame_number as microseconds value
        var timestamp = new TimeVal
        {
            TvSec = (nint)(picture.SystemFrameNumber / 1_000_000),
            TvUsec = (nint)(picture.SystemFrameNumber % 1_000_000)
        };

        // Now acquire media request if needed (buffer is guaranteed to be available)
        MediaRequest? request = null;
        if (_mediaDevice != null)
        {
            request = _device.OutputMPlaneQueue.AcquireMediaRequest();
            SubmitFrameControls(header, pps, sps, dpbSnapshot, picture, accumulatedSliceTypes, request);
        }

        // Queue capture buffer WITHOUT timestamp - driver copies timestamp from output buffer
        _device.CaptureMPlaneQueue.EnqueueDmaBufBuffer(captureBuffer.V4L2Buffer, request, null);

        // Write buffer and enqueue with timestamp
        _device.OutputMPlaneQueue.WriteBufferAndEnqueue(frameData, request, timestamp);
        request?.Queue();

        EnsureStreamingStarted();

        // Add picture to DPB if it's a reference picture
        if (picture.IsRef)
        {
            lock (_dpbLock)
            {
                // Perform sliding window marking before adding new reference
                _dpb.PerformSlidingWindowMarking(_dpb.MaxNumRefFrames);

                // Remove pictures that are no longer references and release their buffers
                // This mirrors GStreamer's gst_h264_dpb_delete_unused
                var removedPictures = _dpb.RemoveUnusedPictures();
                foreach (var removedPic in removedPictures)
                {
                    if (removedPic.Buffer != null)
                    {
                        // If buffer was in pending reuse, return it to available pool now
                        if (_pendingReuse.Remove(removedPic.Buffer))
                        {
                            _bufferToPicture.Remove(removedPic.Buffer);
                            removedPic.Buffer.V4L2Buffer.ResetPlanesUsed();
                            _availableCaptureBuffers.Add(removedPic.Buffer);
                            _logger.LogTrace("Released pending reuse buffer from removed DPB picture frame_num={FrameNum}",
                                removedPic.FrameNum);
                        }
                        else
                        {
                            _bufferToPicture.Remove(removedPic.Buffer);
                        }
                    }
                }

                // Calculate FrameNumWrap and PicNum for short-term reference per H.264 spec 8.2.4.1
                // For newly added picture, FrameNumWrap = frame_num (no wrap-around yet)
                // The wrap-around calculation is more complex for existing DPB entries,
                // but for sliding window we only need consistent ordering.
                picture.FrameNumWrap = (int)picture.FrameNum;

                // PicNum for frames: PicNum = FrameNumWrap (H.264 spec 8.2.4.1)
                // For fields: PicNum = 2 * FrameNumWrap + (bottom_field ? 1 : 0)
                if (picture.FieldPicFlag)
                {
                    picture.PicNum = 2 * picture.FrameNumWrap + (picture.BottomFieldFlag ? 1 : 0);
                }
                else
                {
                    picture.PicNum = picture.FrameNumWrap;
                }

                _dpb.Add(picture);
                _bufferToPicture[captureBuffer] = picture;

                _logger.LogTrace("Added reference picture to DPB: frame_num={FrameNum}, ref_ts={RefTs}, DPB size={Size}",
                    picture.FrameNum, picture.ReferenceTs, _dpb.NumPics);
            }
        }

        _currentPicture = picture;
    }

    private void SubmitFrameControls(
        SliceHeaderState header,
        PpsState pps,
        SpsState sps,
        V4L2H264DpbEntry[] dpbSnapshot,
        H264Picture picture,
        HashSet<uint> accumulatedSliceTypes,
        MediaRequest request)
    {
        var ppsV4L2 = PpsMapper.ConvertPpsStateToV4L2(pps);

        // Ensure scaling matrix flag is set when either SPS or PPS carries scaling data
        bool scalingMatrixPresent = pps.pic_scaling_matrix_present_flag != 0 ||
                                    sps.sps_data.seq_scaling_matrix_present_flag != 0;
        if (_supportsScalingMatrixControl && scalingMatrixPresent)
        {
            ppsV4L2.Flags |= 0x80; // V4L2_H264_PPS_FLAG_SCALING_MATRIX_PRESENT
        }

        _device!.SetSingleExtendedControl(
            V4l2ControlsConstants.V4L2_CID_STATELESS_H264_PPS,
            ppsV4L2,
            request);

        var spsV4L2 = SpsMapper.MapSpsToV4L2(sps);
        _device.SetSingleExtendedControl(
            V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SPS,
            spsV4L2,
            request);

        if (_supportsScalingMatrixControl)
        {
            var scalingMatrix = ScalingMatrixMapper.MapScalingMatrix(sps, pps);
            _device.SetSingleExtendedControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SCALING_MATRIX,
                scalingMatrix,
                request);
        }

        if (_supportsSliceParamsControl)
        {
            var sliceParams = SliceParamsMapper.BuildSliceParams(header, pps, dpbSnapshot);
            _device.SetSingleExtendedControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SLICE_PARAMS,
                sliceParams,
                request);
        }

        var decodeParams = BuildDecodeParams(header, picture, sps, dpbSnapshot, accumulatedSliceTypes);
        _device.SetSingleExtendedControl(
            V4l2ControlsConstants.V4L2_CID_STATELESS_H264_DECODE_PARAMS,
            decodeParams,
            request);
    }

    /// <summary>
    /// Build decode params following GStreamer's gst_v4l2_codec_h264_dec_fill_decoder_params.
    /// </summary>
    private V4L2CtrlH264DecodeParams BuildDecodeParams(
        SliceHeaderState header,
        H264Picture picture,
        SpsState sps,
        V4L2H264DpbEntry[] dpbSnapshot,
        HashSet<uint> accumulatedSliceTypes)
    {
        var decodeParams = new V4L2CtrlH264DecodeParams
        {
            Dpb = dpbSnapshot,
            NalRefIdc = picture.NalRefIdc,
            FrameNum = (ushort)Math.Min(header.frame_num, ushort.MaxValue),
            IdrPicId = (ushort)Math.Min(header.idr_pic_id, ushort.MaxValue),
            PicOrderCntLsb = (ushort)Math.Min(header.pic_order_cnt_lsb, ushort.MaxValue),
            DeltaPicOrderCntBottom = header.delta_pic_order_cnt_bottom,
            DeltaPicOrderCnt0 = header.delta_pic_order_cnt.Count > 0 ? header.delta_pic_order_cnt[0] : 0,
            DeltaPicOrderCnt1 = header.delta_pic_order_cnt.Count > 1 ? header.delta_pic_order_cnt[1] : 0,
            DecRefPicMarkingBitSize = header.dec_ref_pic_marking?.bit_size ?? 0,
            PicOrderCntBitSize = SliceHeaderState.getPicOrderCntLsbLen(sps.sps_data.log2_max_pic_order_cnt_lsb_minus4),
            SliceGroupChangeCycle = header.slice_group_change_cycle,
            Reserved = 0,
            Flags = BuildDecodeFlags(picture, accumulatedSliceTypes)
        };

        // Set field order counts based on picture field type (matching GStreamer)
        switch (picture.Field)
        {
            case H264PictureField.Frame:
                decodeParams.TopFieldOrderCnt = picture.TopFieldOrderCnt;
                decodeParams.BottomFieldOrderCnt = picture.BottomFieldOrderCnt;
                break;
            case H264PictureField.TopField:
                decodeParams.TopFieldOrderCnt = picture.TopFieldOrderCnt;
                decodeParams.BottomFieldOrderCnt = picture.OtherField?.BottomFieldOrderCnt ?? 0;
                break;
            case H264PictureField.BottomField:
                decodeParams.TopFieldOrderCnt = picture.OtherField?.TopFieldOrderCnt ?? 0;
                decodeParams.BottomFieldOrderCnt = picture.BottomFieldOrderCnt;
                break;
        }

        return decodeParams;
    }

    /// <summary>
    /// Build decode flags matching GStreamer's implementation.
    /// In GStreamer, PFRAME/BFRAME flags are accumulated from all slices via |=.
    /// </summary>
    private static uint BuildDecodeFlags(H264Picture picture, HashSet<uint> accumulatedSliceTypes)
    {
        uint flags = 0;

        // IDR picture flag
        if (picture.IsIdr)
        {
            flags |= V4L2H264Constants.V4L2_H264_DECODE_PARAM_FLAG_IDR_PIC;
        }

        // Field picture flags
        if (picture.FieldPicFlag)
        {
            flags |= V4L2H264Constants.V4L2_H264_DECODE_PARAM_FLAG_FIELD_PIC;
        }

        if (picture.BottomFieldFlag)
        {
            flags |= V4L2H264Constants.V4L2_H264_DECODE_PARAM_FLAG_BOTTOM_FIELD;
        }

        // Slice type flags - matching GStreamer's cumulative |= approach
        // slice_type % 5 normalizes SI/SP/I/P/B to 0-4 range
        foreach (var sliceType in accumulatedSliceTypes)
        {
            switch (sliceType)
            {
                case 0: // P slice
                case 3: // SP slice
                    flags |= V4L2H264Constants.V4L2_H264_DECODE_PARAM_FLAG_PFRAME;
                    break;
                case 1: // B slice
                    flags |= V4L2H264Constants.V4L2_H264_DECODE_PARAM_FLAG_BFRAME;
                    break;
                // case 2: I slice - no flag needed
                // case 4: SI slice - no flag needed
            }
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

        if (_streamingStarted)
        {
            _device.OutputMPlaneQueue.StreamOff();
            _device.CaptureMPlaneQueue.StreamOff();
        }

        lock (_dpbLock)
        {
            _pendingReuse.Clear();
            _dpb.Clear();
            _bufferToPicture.Clear();
        }

        _currentPicture = null;
        ResetPendingFrame();
        _availableCaptureBuffers.CompleteAdding();

        UnmapOutputBuffers();
        _device.Dispose();

        _mediaDevice.Dispose();

        _isInitialized = false;
        _streamingStarted = false;
        _logger.LogInformation("Decoder cleanup completed");
    }

    private void UnmapOutputBuffers()
    {
        foreach (var buffer in _device!.OutputMPlaneQueue.BuffersPool.Buffers)
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
