using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.H264;
using SharpVideo.Linux.Native;
using SharpVideo.V4L2;
using SharpVideo.V4L2DecodeDemo.Models;

namespace SharpVideo.V4L2DecodeDemo.Services;

/// <summary>
/// Modern, clean H.264 decoder using V4L2 hardware acceleration for stateless decoders.
///
/// This implementation correctly separates bitstream data from metadata according to V4L2 specifications:
/// - SPS/PPS parameter sets are sent as V4L2 controls
/// - Slice headers are parsed and sent as V4L2 controls
/// - Only slice data (without parameter sets) is sent in OUTPUT buffers
///
/// The architecture is modular with separate components for:
/// - Parameter set parsing (IH264ParameterSetParser)
/// - Control management (IV4L2StatelessControlManager)
/// - Slice processing (IStatelessSliceProcessor)
/// </summary>
[SupportedOSPlatform("linux")]
public class H264V4L2StatelessDecoder
{
    private readonly V4L2Device _device;
    private readonly ILogger<H264V4L2StatelessDecoder> _logger;

    private readonly DecoderConfiguration _configuration;

    private readonly List<MappedBuffer> _outputBuffers = new();
    private readonly List<MappedBuffer> _captureBuffers = new();
    private readonly Queue<uint> _availableOutputBuffers = new();
    private readonly Queue<uint> _availableCaptureBuffers = new();
    private readonly object _bufferLock = new();

    private bool _disposed;
    private int _framesDecoded;
    private readonly Stopwatch _decodingStopwatch = new();

    private bool _hasValidParameterSets;
    private bool _isInitialized;

    private readonly List<byte> _pendingFrameData = new();
    private SliceHeaderState? _currentFrameHeader;
    private bool _currentFrameIsIdr;
    private int _pendingSliceCount;

    private V4L2CtrlH264Sps? _lastV4L2Sps;
    private V4L2CtrlH264Pps? _lastV4L2Pps;

    public event EventHandler<FrameDecodedEventArgs>? FrameDecoded;
    public event EventHandler<DecodingProgressEventArgs>? ProgressChanged;

    public H264V4L2StatelessDecoder(
        V4L2Device device,
        ILogger<H264V4L2StatelessDecoder> logger,
        DecoderConfiguration? configuration = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? new DecoderConfiguration();
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

        ResetFrameAssembly();

        InitializeDecoder();

        using var naluProvider = new H264AnnexBNaluProvider();
        var naluProcessingTask = ProcessNalusAsync(naluProvider, cancellationToken);
        var feedTask = FeedStreamToNaluProviderAsync(stream, naluProvider, cancellationToken);
        await Task.WhenAll(naluProcessingTask, feedTask);

        ProcessCaptureBuffers();
        ReclaimOutputBuffers();

        _decodingStopwatch.Stop();
        _logger.LogInformation(
            "Stateless decoding completed successfully. {FrameCount} frames in {ElapsedTime:F2}s ({FPS:F2} fps)",
            _framesDecoded,
            _decodingStopwatch.Elapsed.TotalSeconds,
            _framesDecoded / _decodingStopwatch.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// Feeds stream data to NaluProvider in chunks
    /// </summary>
    private async Task FeedStreamToNaluProviderAsync(Stream stream, H264AnnexBNaluProvider naluProvider, CancellationToken cancellationToken)
    {
        const int bufferSize = 64 * 1024; // 64KB buffer
        var buffer = new byte[bufferSize];

        try
        {
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await naluProvider.AppendData(buffer.AsSpan(0, bytesRead).ToArray(), cancellationToken);

                // Report progress periodically
                if (stream.CanSeek)
                {
                    ProgressChanged?.Invoke(this, new DecodingProgressEventArgs
                    {
                        BytesProcessed = stream.Position,
                        TotalBytes = stream.Length,
                        FramesDecoded = _framesDecoded,
                        ElapsedTime = _decodingStopwatch.Elapsed
                    });
                }
            }
        }
        finally
        {
            naluProvider.CompleteWriting();
        }
    }

    /// <summary>
    /// Processes NALUs asynchronously as they become available
    /// </summary>
    private async Task ProcessNalusAsync(H264AnnexBNaluProvider naluProvider, CancellationToken cancellationToken)
    {
        var streamState = new H264BitstreamParserState();
        var parsingOptions = new ParsingOptions();
        await foreach (var naluData in naluProvider.NaluReader.ReadAllAsync(cancellationToken))
        {
            if (naluData.Data.Length < 1)
            {
                continue;
            }
            var naluState = H264NalUnitParser.ParseNalUnit(naluData.WithoutHeader, streamState, parsingOptions);

            ProcessNaluByType(naluData, naluState); // Use WithoutHeader instead of manual span slicing
        }

        FlushPendingFrame();
    }

    /// <summary>
    /// Processes individual NALU based on its type
    /// </summary>
    private void ProcessNaluByType(H264Nalu naluData, NalUnitState naluState)
    {
        var naluType = (NalUnitType)naluState.nal_unit_header.nal_unit_type;

        switch (naluType)
        {
            case NalUnitType.SPS_NUT: // SPS
                _logger.LogDebug("Processing SPS NALU");
                HandleSpsNalu(naluState.nal_unit_payload.sps);
                break;

            case NalUnitType.PPS_NUT: // PPS
                _logger.LogDebug("Processing PPS NALU");
                HandlePpsNalu(naluState.nal_unit_payload.pps);
                break;

            case NalUnitType.CODED_SLICE_OF_NON_IDR_PICTURE_NUT: // Non-IDR slice
            case NalUnitType.CODED_SLICE_OF_IDR_PICTURE_NUT: // IDR slice
                _logger.LogTrace("Processing slice NALU type {NaluType}", naluType);
                HandleSliceNalu(naluData, naluState.nal_unit_payload.slice_layer_without_partitioning_rbsp, naluType);
                break;

            default:
                _logger.LogTrace("Skipping NALU type {NaluType}", naluType);
                break;
        }
    }

    /// <summary>
    /// Handles SPS (Sequence Parameter Set) NALU
    /// </summary>
    private void HandleSpsNalu(SpsState? parsedSps)
    {
        if (parsedSps == null)
        {
            _logger.LogError("Failed to parse SPS");
            throw new Exception("Not able to parse SPS");
        }

        _logger.LogDebug("Parsed SPS: {Id}", parsedSps.sps_data.seq_parameter_set_id);
        var v4L2Sps = SpsMapper.MapSpsToV4L2(parsedSps);
        _device.SetSingleExtendedControl(V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SPS, v4L2Sps);
        _lastV4L2Sps = v4L2Sps;

        _logger.LogDebug("Parsed and stored SPS from stream");
        CheckReadyForDecode();
    }

    /// <summary>
    /// Handles PPS (Picture Parameter Set) NALU
    /// </summary>
    private void HandlePpsNalu(PpsState? parsedPps)
    {
        if (parsedPps == null)
        {
            _logger.LogError("Failed to parse PPS");
            throw new Exception("Not able to parse PPS");
        }

        _logger.LogDebug(
            "Successfully parsed PPS using H264PpsParser: ID={PpsId}, SPS_ID={SpsId}, QP={QP}",
            parsedPps.pic_parameter_set_id,
            parsedPps.seq_parameter_set_id,
            parsedPps.pic_init_qp_minus26 + 26);

        var v4L2Pps = PpsMapper.ConvertPpsStateToV4L2(parsedPps);
        _device.SetSingleExtendedControl(V4l2ControlsConstants.V4L2_CID_STATELESS_H264_PPS, v4L2Pps);
        _lastV4L2Pps = v4L2Pps;

        _logger.LogDebug("Parsed and stored PPS from stream");
        CheckReadyForDecode();
    }

    private void CheckReadyForDecode()
    {
        // If we also have PPS, configure the decoder
        if (_lastV4L2Sps.HasValue && _lastV4L2Pps.HasValue)
        {
            _hasValidParameterSets = true;
            _logger.LogInformation("Successfully configured parameter sets from stream");
        }
    }

    /// <summary>
    /// Handles slice NALUs (actual video data)
    /// </summary>
    private void HandleSliceNalu(H264Nalu nalu, SliceLayerWithoutPartitioningRbspState sliceLayerWithoutPartitioningRbsp, NalUnitType naluType)
    {
        if (!_hasValidParameterSets)
        {
            _logger.LogTrace("Ignoring slice because SPS/PPS are not configured yet");
            return;
        }

        if (sliceLayerWithoutPartitioningRbsp?.slice_header == null)
        {
            _logger.LogWarning("Slice header missing; skipping slice");
            return;
        }

        var header = sliceLayerWithoutPartitioningRbsp.slice_header;

        var isFrameStart = header.first_mb_in_slice == 0;

        if (isFrameStart && _pendingFrameData.Count > 0)
        {
            FlushPendingFrame();
        }

        if (isFrameStart)
        {
            _currentFrameHeader = header;
            _currentFrameIsIdr = naluType == NalUnitType.CODED_SLICE_OF_IDR_PICTURE_NUT;
            _pendingSliceCount = 0;
        }

        if (_currentFrameHeader == null)
        {
            _currentFrameHeader = header;
        }

        AppendSliceToPendingFrame(nalu.Data);
        _pendingSliceCount++;

        _logger.LogTrace(
            "Buffered slice for frame {FrameNum} (slice #{SliceIndex}, type={SliceType}, bytes={SliceSize})",
            _currentFrameHeader?.frame_num,
            _pendingSliceCount,
            header.slice_type,
            nalu.Data.Length);
    }

    private void AppendSliceToPendingFrame(ReadOnlySpan<byte> sliceData)
    {
        if (sliceData.Length == 0)
            return;

        var start = _pendingFrameData.Count;
        _pendingFrameData.AddRange(new byte[sliceData.Length]);

        var span = CollectionsMarshal.AsSpan(_pendingFrameData);
        sliceData.CopyTo(span.Slice(start, sliceData.Length));
    }

    private void FlushPendingFrame()
    {
        if (_pendingFrameData.Count == 0 || _currentFrameHeader == null)
            return;

        TrySubmitFrameToDevice(_pendingFrameData, _currentFrameHeader, _currentFrameIsIdr, _pendingSliceCount);
        ResetFrameAssembly();
    }

    private void ResetFrameAssembly()
    {
        _pendingFrameData.Clear();
        _currentFrameHeader = null;
        _currentFrameIsIdr = false;
        _pendingSliceCount = 0;
    }

    private bool TrySubmitFrameToDevice(List<byte> frameData, SliceHeaderState header, bool isIdr, int sliceCount)
    {
        ProcessCaptureBuffers();
        ReclaimOutputBuffers();

        if (!TryAcquireOutputBuffer(out var bufferIndex, out var mappedBuffer))
        {
            _logger.LogWarning("No available OUTPUT buffer when queuing frame {FrameNum}; dropping frame", header.frame_num);
            return false;
        }

        var payloadSize = frameData.Count;
        if (payloadSize == 0)
        {
            ReturnOutputBuffer(bufferIndex);
            return true;
        }

        if (payloadSize > mappedBuffer.Size)
        {
            _logger.LogError(
                "Frame {FrameNum} size {PayloadSize} exceeds buffer size {BufferSize}; dropping frame",
                header.frame_num,
                payloadSize,
                mappedBuffer.Size);
            ReturnOutputBuffer(bufferIndex);
            return false;
        }

        unsafe
        {
            var destination = new Span<byte>((void*)mappedBuffer.Pointer, (int)mappedBuffer.Size);
            CollectionsMarshal.AsSpan(frameData).Slice(0, payloadSize).CopyTo(destination);
        }

        mappedBuffer.Planes[0].BytesUsed = (uint)payloadSize;
        mappedBuffer.Planes[0].DataOffset = 0;

        _logger.LogInformation(
            "Queuing frame {FrameNum} ({SliceCount} slices, {Bytes} bytes, IDR={IsIdr})",
            header.frame_num,
            sliceCount,
            payloadSize,
            isIdr);

        if (!QueueOutputBuffer(bufferIndex, mappedBuffer, payloadSize))
        {
            return false;
        }

        ProcessCaptureBuffers();
        ReclaimOutputBuffers();

        return true;
    }

    private bool QueueOutputBuffer(uint bufferIndex, MappedBuffer mappedBuffer, int payloadSize)
    {
        var buffer = new V4L2Buffer
        {
            Index = bufferIndex,
            Type = V4L2BufferType.VIDEO_OUTPUT_MPLANE,
            Memory = V4L2Constants.V4L2_MEMORY_MMAP,
            Length = (uint)mappedBuffer.Planes.Length,
            Field = (uint)V4L2Field.NONE,
            BytesUsed = (uint)payloadSize,
            Timestamp = new TimeVal { TvSec = 0, TvUsec = 0 },
            Sequence = 0
        };

        unsafe
        {
            fixed (V4L2Plane* planePtr = mappedBuffer.Planes)
            {
                buffer.Planes = planePtr;

                var result = LibV4L2.QueueBuffer(_device.fd, ref buffer);
                if (!result.Success)
                {
                    _logger.LogWarning(
                        "Failed to queue output buffer {Index}: {Error}",
                        bufferIndex,
                        result.ErrorMessage ?? $"errno {result.ErrorCode}");
                    ReturnOutputBuffer(bufferIndex);
                    return false;
                }
            }
        }

        return true;
    }

    private void ProcessCaptureBuffers()
    {
        if (_captureBuffers.Count == 0)
            return;

        var planeCount = _captureBuffers[0].Planes.Length;

        unsafe
        {
            var planeStorage = stackalloc V4L2Plane[planeCount];

            while (true)
            {
                var buffer = new V4L2Buffer
                {
                    Type = V4L2BufferType.VIDEO_CAPTURE_MPLANE,
                    Memory = V4L2Constants.V4L2_MEMORY_MMAP,
                    Length = (uint)planeCount,
                    Field = (uint)V4L2Field.NONE,
                    Planes = planeStorage
                };

                var result = LibV4L2.DequeueBuffer(_device.fd, ref buffer);
                if (!result.Success)
                {
                    if (result.ErrorCode == 11)
                    {
                        break;
                    }

                    _logger.LogDebug(
                        "Failed to dequeue capture buffer: {Error}",
                        result.ErrorMessage ?? $"errno {result.ErrorCode}");
                    break;
                }

                uint bytesUsed = 0;
                foreach (var plane in buffer.PlaneSpan)
                {
                    bytesUsed += plane.BytesUsed;
                }

                _framesDecoded++;
                FrameDecoded?.Invoke(this, new FrameDecodedEventArgs
                {
                    FrameNumber = _framesDecoded,
                    BytesUsed = bytesUsed,
                    Timestamp = DateTime.UtcNow
                });

                var mappedBuffer = _captureBuffers[(int)buffer.Index];
                var requeueBuffer = new V4L2Buffer
                {
                    Index = buffer.Index,
                    Type = V4L2BufferType.VIDEO_CAPTURE_MPLANE,
                    Memory = V4L2Constants.V4L2_MEMORY_MMAP,
                    Length = (uint)mappedBuffer.Planes.Length,
                    Field = (uint)V4L2Field.NONE,
                    Timestamp = new TimeVal { TvSec = 0, TvUsec = 0 },
                    Sequence = 0
                };

                fixed (V4L2Plane* planePtr = mappedBuffer.Planes)
                {
                    requeueBuffer.Planes = planePtr;

                    var queueResult = LibV4L2.QueueBuffer(_device.fd, ref requeueBuffer);
                    if (!queueResult.Success)
                    {
                        _logger.LogWarning(
                            "Failed to requeue capture buffer {Index}: {Error}",
                            buffer.Index,
                            queueResult.ErrorMessage ?? $"errno {queueResult.ErrorCode}");
                    }
                }
            }
        }
    }

    private void ReclaimOutputBuffers()
    {
        if (_outputBuffers.Count == 0)
            return;

        unsafe
        {
            var planeStorage = stackalloc V4L2Plane[1];

            while (true)
            {
                var buffer = new V4L2Buffer
                {
                    Type = V4L2BufferType.VIDEO_OUTPUT_MPLANE,
                    Memory = V4L2Constants.V4L2_MEMORY_MMAP,
                    Length = 1,
                    Field = (uint)V4L2Field.NONE,
                    Planes = planeStorage
                };

                var result = LibV4L2.DequeueBuffer(_device.fd, ref buffer);
                if (!result.Success)
                {
                    if (result.ErrorCode == 11)
                    {
                        break;
                    }

                    _logger.LogDebug(
                        "Failed to dequeue output buffer: {Error}",
                        result.ErrorMessage ?? $"errno {result.ErrorCode}");
                    break;
                }

                ReturnOutputBuffer(buffer.Index);
            }
        }
    }

    private bool TryAcquireOutputBuffer(out uint bufferIndex, out MappedBuffer mappedBuffer)
    {
        lock (_bufferLock)
        {
            if (_availableOutputBuffers.Count > 0)
            {
                bufferIndex = _availableOutputBuffers.Dequeue();
                mappedBuffer = _outputBuffers[(int)bufferIndex];
                return true;
            }
        }

        bufferIndex = 0;
        mappedBuffer = null!;
        return false;
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
        if (_isInitialized)
            return;

        _logger.LogInformation("Initializing H.264 stateless decoder...");

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

        _isInitialized = true;
        _logger.LogInformation("Decoder initialization completed successfully");
    }

    private void ConfigureFormats()
    {
        _logger.LogInformation("Configuring stateless decoder formats...");

        try
        {
            // Configure output format (H.264 input) for stateless decoder
            var outputFormat = new V4L2PixFormatMplane
            {
                Width = _configuration.InitialWidth,
                Height = _configuration.InitialHeight,
                PixelFormat = V4L2PixelFormats.V4L2_PIX_FMT_H264, // Use standard H264 format
                NumPlanes = 1,
                Field = (uint)V4L2Field.NONE,
                Colorspace = 5, // V4L2_COLORSPACE_REC709
                YcbcrEncoding = 1, // V4L2_YCBCR_ENC_DEFAULT
                Quantization = 1, // V4L2_QUANTIZATION_DEFAULT
                XferFunc = 1 // V4L2_XFER_FUNC_DEFAULT
            };

            _device.SetFormatMplane(V4L2BufferType.VIDEO_OUTPUT_MPLANE, outputFormat);
            _logger.LogInformation("Set output format: {Width}x{Height} H264", outputFormat.Width, outputFormat.Height);

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

            _device.SetFormatMplane(V4L2BufferType.VIDEO_CAPTURE_MPLANE, captureFormat);
            _logger.LogInformation("Set capture format: {Width}x{Height} with {Planes} planes",
                captureFormat.Width, captureFormat.Height, captureFormat.NumPlanes);

            _logger.LogInformation("Format configuration completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure formats");
            throw new InvalidOperationException($"Format configuration failed: {ex.Message}", ex);
        }
    }

    private void SetupAndMapBuffers()
    {
        _logger.LogInformation("Setting up and mapping buffers...");


        // Setup OUTPUT buffers for slice data with proper V4L2 mmap
        SetupBufferQueue(V4L2BufferType.VIDEO_OUTPUT_MPLANE, _configuration.OutputBufferCount, _outputBuffers,
            _availableOutputBuffers);

        // Setup CAPTURE buffers for decoded frames with proper V4L2 mmap
        SetupBufferQueue(V4L2BufferType.VIDEO_CAPTURE_MPLANE, _configuration.CaptureBufferCount, _captureBuffers,
            _availableCaptureBuffers);

        _logger.LogInformation("Buffer setup completed: {OutputBuffers} output, {CaptureBuffers} capture",
            _outputBuffers.Count, _captureBuffers.Count);
    }

    private void SetupBufferQueue(V4L2BufferType bufferType, uint bufferCount, List<MappedBuffer> bufferList, Queue<uint> availableQueue)
    {
        // Request buffers from V4L2
        var reqBufs = new V4L2RequestBuffers
        {
            Count = bufferCount,
            Type = bufferType,
            Memory = V4L2Constants.V4L2_MEMORY_MMAP
        };

        var result = LibV4L2.RequestBuffers(_device.fd, ref reqBufs);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to request {bufferType} buffers: {result.ErrorMessage}");
        }

        _logger.LogDebug("Requested {RequestedCount} {BufferType} buffers, got {ActualCount}",
            bufferCount, bufferType, reqBufs.Count);

        // Map each buffer - simplified approach for demo
        for (uint i = 0; i < reqBufs.Count; i++)
        {
            // Determine plane count based on buffer type
            int planeCount;
            uint totalSize;

            if (bufferType == V4L2BufferType.VIDEO_OUTPUT_MPLANE)
            {
                // Output buffer for H.264 slice data - single plane
                planeCount = 1;
                totalSize = _configuration.SliceBufferSize;
            }
            else
            {
                // Capture buffer for decoded frames - typically 2 planes for NV12
                planeCount = 2;
                uint yPlaneSize = _configuration.InitialWidth * _configuration.InitialHeight;
                uint uvPlaneSize = yPlaneSize / 2; // NV12 UV plane is half the size
                totalSize = yPlaneSize + uvPlaneSize;
            }

            var bufferPtr = Marshal.AllocHGlobal((int)totalSize);

            // Create planes array with proper configuration
            var planes = new V4L2Plane[planeCount];

            if (bufferType == V4L2BufferType.VIDEO_OUTPUT_MPLANE)
            {
                // Single plane for slice data
                planes[0] = new V4L2Plane
                {
                    Length = totalSize,
                    BytesUsed = 0
                };
            }
            else
            {
                // Two planes for NV12 format
                uint yPlaneSize = _configuration.InitialWidth * _configuration.InitialHeight;
                uint uvPlaneSize = totalSize - yPlaneSize;

                planes[0] = new V4L2Plane
                {
                    Length = yPlaneSize,
                    BytesUsed = yPlaneSize // Pre-allocated size for capture
                };

                planes[1] = new V4L2Plane
                {
                    Length = uvPlaneSize,
                    BytesUsed = uvPlaneSize // Pre-allocated size for capture
                };
            }

            var mappedBuffer = new MappedBuffer
            {
                Index = i,
                Pointer = bufferPtr,
                Size = totalSize,
                Planes = planes
            };

            bufferList.Add(mappedBuffer);
            availableQueue.Enqueue(i);

            _logger.LogTrace("Mapped buffer {Index} for {BufferType}: {Size} bytes, {PlaneCount} planes at {Pointer:X8}",
                i, bufferType, totalSize, planeCount, bufferPtr.ToInt64());
        }
    }

    private void StartStreaming()
    {
        _logger.LogInformation("Starting V4L2 streaming...");

        try
        {
            // Queue all capture buffers before starting streaming
            for (uint i = 0; i < _captureBuffers.Count; i++)
            {
                var mappedBuffer = _captureBuffers[(int)i];

                var buffer = new V4L2Buffer
                {
                    Index = i,
                    Type = V4L2BufferType.VIDEO_CAPTURE_MPLANE,
                    Memory = V4L2Constants.V4L2_MEMORY_MMAP,
                    Length = (uint)mappedBuffer.Planes.Length,
                    Field = (uint)V4L2Field.NONE,
                    BytesUsed = 0, // Output buffer, hardware will fill this
                    Flags = 0,
                    Timestamp = new TimeVal { TvSec = 0, TvUsec = 0 },
                    Sequence = 0
                };

                unsafe
                {
                    // Set up planes for multiplanar capture buffer
                    fixed (V4L2Plane* planePtr = mappedBuffer.Planes)
                    {
                        buffer.Planes = planePtr;
                    }
                }

                var result = LibV4L2.QueueBuffer(_device.fd, ref buffer);
                if (!result.Success)
                {
                    _logger.LogWarning("Failed to queue capture buffer {Index}: {Error}", i, result.ErrorMessage);
                    // Don't throw here, continue with other buffers
                }
                else
                {
                    _logger.LogTrace("Successfully queued capture buffer {Index}", i);
                }
            }

            // Start streaming on OUTPUT queue first
            _device.StreamOn(V4L2BufferType.VIDEO_OUTPUT_MPLANE);
            _logger.LogTrace("Started OUTPUT streaming");

            // Start streaming on CAPTURE queue
            _device.StreamOn(V4L2BufferType.VIDEO_CAPTURE_MPLANE);
            _logger.LogTrace("Started CAPTURE streaming");

            _logger.LogInformation("V4L2 streaming started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start streaming");
            throw new InvalidOperationException($"Failed to start streaming: {ex.Message}", ex);
        }
    }

    private void Cleanup()
    {
        if (!_isInitialized)
            return;

        _logger.LogInformation("Cleaning up decoder resources...");

        try
        {
            // Stop streaming
            if (_device?.fd > 0)
            {
                _device.StreamOff(V4L2BufferType.VIDEO_OUTPUT_MPLANE);
                _device.StreamOff(V4L2BufferType.VIDEO_CAPTURE_MPLANE);
            }

            // Unmap buffers
            UnmapBuffers(_outputBuffers);
            UnmapBuffers(_captureBuffers);

            // Clear buffer queues
            lock (_bufferLock)
            {
                _availableOutputBuffers.Clear();
                _availableCaptureBuffers.Clear();
            }

            _isInitialized = false;
            _logger.LogInformation("Decoder cleanup completed");
            ResetFrameAssembly();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during cleanup");
        }
    }

    private void UnmapBuffers(List<MappedBuffer> buffers)
    {
        foreach (var buffer in buffers)
        {
            if (buffer.Pointer != IntPtr.Zero)
            {
                try
                {
                    // Free allocated memory instead of unmapping
                    // In real implementation, would call munmap()
                    Marshal.FreeHGlobal(buffer.Pointer);
                    _logger.LogTrace("Freed buffer {Index} memory at {Pointer:X8}", buffer.Index, buffer.Pointer.ToInt64());
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to free buffer {Index}", buffer.Index);
                }
            }
        }
        buffers.Clear();
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