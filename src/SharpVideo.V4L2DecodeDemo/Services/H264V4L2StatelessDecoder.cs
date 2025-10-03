using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    private readonly ILogger<H264V4L2StatelessDecoder> _logger;

    private readonly DecoderConfiguration _configuration;

    private readonly List<MappedBuffer> _outputBuffers = new();
    private readonly List<MappedBuffer> _captureBuffers = new();
    private readonly Queue<uint> _availableOutputBuffers = new();
    private readonly Queue<uint> _availableCaptureBuffers = new();
    private readonly Queue<MediaRequestContext> _availableRequests = new();
    private readonly Dictionary<uint, MediaRequestContext> _inFlightRequests = new();
    private readonly object _bufferLock = new();

    private int _outputPlaneCount = 1;
    private int _capturePlaneCount = 1;

    private bool _disposed;
    private int _framesDecoded;
    private readonly Stopwatch _decodingStopwatch = new();

    private bool _hasValidParameterSets;
    private bool _isInitialized;

    private bool _requestApiEnabled;
    private int _mediaFd = -1;
    private bool _supportsSliceParamsControl;

    private V4L2CtrlH264Sps? _lastV4L2Sps;
    private V4L2CtrlH264Pps? _lastV4L2Pps;

    private int _consecutiveFailures = 0;
    private const int MAX_CONSECUTIVE_FAILURES = 10;

    // DPB (Decoded Picture Buffer) tracking
    private readonly List<DpbEntry> _dpb = new();
    private const int MAX_DPB_SIZE = 16;
    private uint _maxFrameNum = 256;

    private class DpbEntry
    {
        public uint FrameNum { get; set; }
        public uint PicOrderCnt { get; set; }
        public bool IsReference { get; set; }
        public bool IsLongTerm { get; set; }
    }

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
        _supportsSliceParamsControl = device.ExtendedControls.Any(c => c.Id == V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SLICE_PARAMS);
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

        ProcessCaptureBuffers();
        ReclaimOutputBuffers();

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
    private async Task FeedStreamToNaluProviderAsync(Stream stream, H264AnnexBNaluProvider naluProvider, CancellationToken cancellationToken)
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
                NalUnitState? naluState = null;
                try
                {
                    naluState = H264NalUnitParser.ParseNalUnit(naluData.WithoutHeader, streamState, parsingOptions);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse NALU #{Index} (length {Length})", naluCount + 1, naluData.Data.Length);
                    continue;
                }

                if (naluState == null)
                {
                    _logger.LogWarning("Parser returned null for NALU #{Index}; skipping", naluCount + 1);
                    continue;
                }

                naluCount++;

                if (naluCount <= 5 || naluCount % 100 == 0)
                {
                    _logger.LogInformation(
                        "Processing NALU #{Index}: type={Type} length={Length}",
                        naluCount,
                        (NalUnitType)naluState.nal_unit_header.nal_unit_type,
                        naluData.Data.Length);
                }

            _logger.LogDebug("Dispatching NALU #{Index} to handler", naluCount);
            ProcessNaluByType(naluData, naluState); // Use WithoutHeader instead of manual span slicing
        }

        _logger.LogInformation("Finished processing NALUs ({Count} total)", naluCount);
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
        _lastV4L2Sps = v4L2Sps;

        // Update max_frame_num from SPS
        _maxFrameNum = (uint)(1 << (int)(parsedSps.sps_data.log2_max_frame_num_minus4 + 4));

        _logger.LogDebug("Parsed and stored SPS from stream (max_frame_num={MaxFrameNum})", _maxFrameNum);
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
        _lastV4L2Pps = v4L2Pps;

        _logger.LogDebug("Parsed and stored PPS from stream");
        CheckReadyForDecode();
    }

    private void CheckReadyForDecode()
    {
        // If we have both SPS and PPS, we're ready to decode
        if (_lastV4L2Sps.HasValue && _lastV4L2Pps.HasValue)
        {
            _hasValidParameterSets = true;
            _logger.LogInformation("Successfully configured parameter sets from stream - decoder ready for frames");
        }
    }

    /// <summary>
    /// Handles slice NALUs (actual video data)
    /// </summary>
    private void HandleSliceNalu(H264Nalu nalu, SliceLayerWithoutPartitioningRbspState sliceLayerWithoutPartitioningRbsp, NalUnitType naluType)
    {
        if (!_hasValidParameterSets)
        {
            var pendingFrame = sliceLayerWithoutPartitioningRbsp?.slice_header?.frame_num;
            _logger.LogDebug(
                "Ignoring slice NALU because SPS/PPS not yet configured (frame_num={Frame})",
                pendingFrame.HasValue ? pendingFrame.Value : -1);
            return;
        }

        if (sliceLayerWithoutPartitioningRbsp?.slice_header == null)
        {
            _logger.LogWarning("Slice header missing; skipping slice");
            return;
        }

        var header = sliceLayerWithoutPartitioningRbsp.slice_header;
        if (header.first_mb_in_slice != 0)
        {
            _logger.LogDebug("Skipping non-initial slice for frame {FrameNum} in frame-based mode", header.frame_num);
            return;
        }
        var isIdr = naluType == NalUnitType.CODED_SLICE_OF_IDR_PICTURE_NUT;

        // Verify we still have valid parameter sets
        if (!_lastV4L2Sps.HasValue || !_lastV4L2Pps.HasValue)
        {
            _logger.LogWarning("Lost parameter sets during decoding, cannot process frame {FrameNum}", header.frame_num);
            return;
        }

        // Reclaim any OUTPUT buffers that the driver has finished processing
        ReclaimOutputBuffers();
        ProcessCaptureBuffers();

        // Try to submit frame with simple retry logic
        if (!TrySubmitFrameToDevice(nalu.Data, header, isIdr))
        {
            _consecutiveFailures++;
            _logger.LogDebug("First frame submission failed ({Failures} consecutive), attempting retry for frame {FrameNum}",
                _consecutiveFailures, header.frame_num);

            // Single retry with buffer reclamation
            ReclaimOutputBuffers();
            Thread.Sleep(2); // Slightly longer delay

            if (!TrySubmitFrameToDevice(nalu.Data, header, isIdr))
            {
                _consecutiveFailures++;
                _logger.LogWarning("Failed to queue frame {FrameNum} after retry; dropping (consecutive failures: {Failures})",
                    header.frame_num, _consecutiveFailures);

                // If we have too many consecutive failures, reset the decoder
                if (_consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                {
                    _logger.LogError("Too many consecutive failures ({Failures}), resetting decoder", _consecutiveFailures);
                    ResetDecoderState();
                    _consecutiveFailures = 0;
                }
                return;
            }
        }

        // Reset failure counter on success
        _consecutiveFailures = 0;        // Drain CAPTURE buffers that are ready after queuing this frame
        ProcessCaptureBuffers();
    }

    private bool TrySubmitFrameToDevice(ReadOnlySpan<byte> frameData, SliceHeaderState header, bool isIdr)
    {
        if (!TryAcquireOutputBuffer(out var bufferIndex, out var mappedBuffer))
        {
            _logger.LogWarning("No available OUTPUT buffer when queuing frame {FrameNum}; dropping frame", header.frame_num);
            return false;
        }

        MediaRequestContext? requestContext = null;
        int requestFd = -1;

        var payloadSize = frameData.Length;
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
            frameData.CopyTo(destination);
        }

        mappedBuffer.Planes[0].BytesUsed = (uint)payloadSize;
        mappedBuffer.Planes[0].Length = mappedBuffer.PlaneSizes[0];
        mappedBuffer.Planes[0].DataOffset = 0;

        if (_requestApiEnabled)
        {
            if (!TryAcquireMediaRequest(out var acquiredRequest))
            {
                _logger.LogWarning("Failed to acquire media request for frame {FrameNum}; dropping frame", header.frame_num);
                ReturnOutputBuffer(bufferIndex);
                return false;
            }

            requestContext = acquiredRequest;
            requestContext.InUse = true;
            requestContext.BufferIndex = bufferIndex;
            requestFd = requestContext.RequestFd;

            if (!SubmitFrameControls(header, isIdr, requestContext))
            {
                ReturnOutputBuffer(bufferIndex);
                requestContext.InUse = false;
                var reinitResult = LibMedia.ReinitRequest(requestContext.RequestFd);
                if (!reinitResult.Success)
                {
                    _logger.LogWarning(
                        "MEDIA_REQUEST_IOC_REINIT failed after control submission failure for request fd {Fd}: {Error}",
                        requestContext.RequestFd,
                        reinitResult.ErrorMessage ?? $"errno {reinitResult.ErrorCode}");
                    Libc.close(requestContext.RequestFd);
                    TryAllocateMediaRequest();
                }
                else
                {
                    ReleaseMediaRequest(requestContext);
                }
                return false;
            }
        }

        _logger.LogInformation(
            "Queuing frame {FrameNum} ({Bytes} bytes, IDR={IsIdr})",
            header.frame_num,
            payloadSize,
            isIdr);

        if (!QueueOutputBuffer(bufferIndex, mappedBuffer, requestFd))
        {
            if (requestContext != null)
            {
                requestContext.InUse = false;
                var reinitResult = LibMedia.ReinitRequest(requestContext.RequestFd);
                if (!reinitResult.Success)
                {
                    _logger.LogWarning(
                        "MEDIA_REQUEST_IOC_REINIT failed after queue failure for request fd {Fd}: {Error}",
                        requestContext.RequestFd,
                        reinitResult.ErrorMessage ?? $"errno {reinitResult.ErrorCode}");
                    Libc.close(requestContext.RequestFd);
                    TryAllocateMediaRequest();
                }
                else
                {
                    ReleaseMediaRequest(requestContext);
                }
            }
            return false;
        }

        if (requestContext != null)
        {
            var queueResult = LibMedia.QueueRequest(requestContext.RequestFd);
            if (!queueResult.Success)
            {
                _logger.LogWarning(
                    "MEDIA_REQUEST_IOC_QUEUE failed for buffer {Index}: {Error}",
                    bufferIndex,
                    queueResult.ErrorMessage ?? $"errno {queueResult.ErrorCode}");

                var reinitResult = LibMedia.ReinitRequest(requestContext.RequestFd);
                if (!reinitResult.Success)
                {
                    _logger.LogWarning(
                        "Unable to reinitialize failed request fd {Fd}: {Error}",
                        requestContext.RequestFd,
                        reinitResult.ErrorMessage ?? $"errno {reinitResult.ErrorCode}");
                    Libc.close(requestContext.RequestFd);
                    TryAllocateMediaRequest();
                }
                else
                {
                    requestContext.InUse = false;
                    ReleaseMediaRequest(requestContext);
                }

                ReturnOutputBuffer(bufferIndex);
                return false;
            }

            lock (_bufferLock)
            {
                _inFlightRequests[bufferIndex] = requestContext;
            }
        }

        return true;
    }

    private bool SubmitFrameControls(SliceHeaderState header, bool isIdr, MediaRequestContext request)
    {
        try
        {
            if (!_lastV4L2Sps.HasValue || !_lastV4L2Pps.HasValue)
            {
                _logger.LogError("Cannot submit frame controls without SPS/PPS");
                return false;
            }

            _logger.LogDebug("Setting controls for frame {FrameNum} (IDR={IsIdr}, request_fd={RequestFd})",
                header.frame_num, isIdr, request.RequestFd);

            // Set SPS, PPS, and DECODE_PARAMS together on this request
            // This is critical for stateless decoders - all metadata must be per-request
            try
            {
                _device.SetSingleExtendedControl(V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SPS, _lastV4L2Sps.Value, request.RequestFd);
                _logger.LogDebug("SPS control set successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set SPS control");
                throw;
            }

            try
            {
                _device.SetSingleExtendedControl(V4l2ControlsConstants.V4L2_CID_STATELESS_H264_PPS, _lastV4L2Pps.Value, request.RequestFd);
                _logger.LogDebug("PPS control set successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set PPS control");
                throw;
            }

            if (_supportsSliceParamsControl)
            {
                var sliceParams = BuildSliceParams(header);
                try
                {
                    _device.SetSingleExtendedControl(V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SLICE_PARAMS, sliceParams, request.RequestFd);
                    _logger.LogDebug("SLICE_PARAMS control set successfully");
                }
                catch (InvalidOperationException ex) when (IsInvalidArgument(ex))
                {
                    _supportsSliceParamsControl = false;
                    _logger.LogInformation("Device rejected slice parameters control; disabling it. {Message}", ex.Message);
                }
            }

            var decodeParams = BuildDecodeParams(header, isIdr);
            _logger.LogDebug("DECODE_PARAMS: FrameNum={FrameNum}, NalRefIdc={NalRefIdc}, IDR={IsIdr}, DPB_count={DpbCount}",
                decodeParams.FrameNum, decodeParams.NalRefIdc,
                (decodeParams.Flags & V4L2H264Constants.V4L2_H264_DECODE_PARAM_FLAG_IDR_PIC) != 0,
                _dpb.Count);

            try
            {
                _device.SetSingleExtendedControl(V4l2ControlsConstants.V4L2_CID_STATELESS_H264_DECODE_PARAMS, decodeParams, request.RequestFd);
                _logger.LogDebug("DECODE_PARAMS control set successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set DECODE_PARAMS control");
                throw;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to submit stateless controls for frame {FrameNum}", header.frame_num);
            return false;
        }
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

    private V4L2CtrlH264DecodeParams BuildDecodeParams(SliceHeaderState header, bool isIdr)
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
            _logger.LogTrace("Added reference frame to DPB: frame_num={FrameNum}, DPB size={Size}", header.frame_num, _dpb.Count);
        }

        // Manage DPB size - remove oldest frames if we exceed max size
        var maxDpbSize = _lastV4L2Sps.HasValue && _lastV4L2Sps.Value.max_num_ref_frames > 0
            ? (int)_lastV4L2Sps.Value.max_num_ref_frames
            : 4;

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

    private static bool IsInvalidArgument(Exception ex)
    {
        return ex is InvalidOperationException ioe && ioe.Message.Contains("EINVAL", StringComparison.OrdinalIgnoreCase);
    }

    private bool QueueOutputBuffer(uint bufferIndex, MappedBuffer mappedBuffer, int requestFd)
    {
        // Validate buffer state before queuing
        if (bufferIndex >= _outputBuffers.Count)
        {
            _logger.LogError("Invalid buffer index {Index}, max is {Max}", bufferIndex, _outputBuffers.Count - 1);
            return false;
        }

        if (mappedBuffer.Planes.Length == 0 || mappedBuffer.Planes[0].BytesUsed == 0)
        {
            _logger.LogWarning("Buffer {Index} has no data to queue", bufferIndex);
            return false;
        }

        _logger.LogDebug("Queuing buffer {Index} with {Bytes} bytes, requestFd={RequestFd}",
            bufferIndex, mappedBuffer.Planes[0].BytesUsed, requestFd);

        var buffer = new V4L2Buffer
        {
            Index = bufferIndex,
            Type = V4L2BufferType.VIDEO_OUTPUT_MPLANE,
            Memory = V4L2Constants.V4L2_MEMORY_MMAP,
            Length = (uint)mappedBuffer.Planes.Length,
            Field = (uint)V4L2Field.NONE,
            Flags = (uint)V4L2BufferFlags.REQUEST_FD,
            BytesUsed = 0,
            Timestamp = new TimeVal { TvSec = 0, TvUsec = 0 },
            Sequence = 0,
            RequestFd = requestFd
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
                        "Failed to queue output buffer {Index}: {Error} (errno: {ErrorCode})",
                        bufferIndex,
                        result.ErrorMessage ?? "Unknown error",
                        result.ErrorCode);

                    // For specific errors, try to recover
                    if (result.ErrorCode == 16) // EBUSY
                    {
                        _logger.LogDebug("Attempting buffer recovery for EBUSY error");
                        ReclaimOutputBuffers();

                        // Small delay to let driver stabilize
                        Thread.Sleep(1);

                        // Retry once
                        result = LibV4L2.QueueBuffer(_device.fd, ref buffer);
                        if (result.Success)
                        {
                            _logger.LogDebug("Buffer queue retry succeeded for buffer {Index}", bufferIndex);
                            return true;
                        }

                        _logger.LogWarning("Buffer queue retry failed for buffer {Index}: {Error}",
                            bufferIndex, result.ErrorMessage ?? $"errno {result.ErrorCode}");
                    }

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

                for (int p = 0; p < mappedBuffer.Planes.Length; p++)
                {
                    mappedBuffer.Planes[p].BytesUsed = 0;
                    mappedBuffer.Planes[p].Length = mappedBuffer.PlaneSizes[p];
                }

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
                    if (result.ErrorCode == 11 || result.ErrorCode == 35) // EAGAIN or EWOULDBLOCK
                    {
                        break;
                    }

                    _logger.LogDebug(
                        "Failed to dequeue output buffer: {Error}",
                        result.ErrorMessage ?? $"errno {result.ErrorCode}");
                    break;
                }

                ReturnOutputBuffer(buffer.Index);

                if (_requestApiEnabled)
                {
                    MediaRequestContext? request = null;
                    lock (_bufferLock)
                    {
                        if (_inFlightRequests.TryGetValue(buffer.Index, out var existing))
                        {
                            request = existing;
                            _inFlightRequests.Remove(buffer.Index);
                        }
                    }

                    if (request != null)
                    {
                        request.InUse = false;
                        var reinitResult = LibMedia.ReinitRequest(request.RequestFd);
                        if (!reinitResult.Success)
                        {
                            _logger.LogWarning(
                                "MEDIA_REQUEST_IOC_REINIT failed for request fd {Fd}: {Error}",
                                request.RequestFd,
                                reinitResult.ErrorMessage ?? $"errno {reinitResult.ErrorCode}");
                            Libc.close(request.RequestFd);
                            TryAllocateMediaRequest();
                        }
                        else
                        {
                            ReleaseMediaRequest(request);
                        }
                    }
                }
            }
        }
    }

    private bool TryAcquireOutputBuffer(out uint bufferIndex, out MappedBuffer mappedBuffer)
    {
        // First try to reclaim any processed buffers
        ReclaimOutputBuffers();

        lock (_bufferLock)
        {
            if (_availableOutputBuffers.Count > 0)
            {
                bufferIndex = _availableOutputBuffers.Dequeue();
                mappedBuffer = _outputBuffers[(int)bufferIndex];
                _logger.LogTrace("Acquired buffer {Index}, {Available} buffers remaining",
                    bufferIndex, _availableOutputBuffers.Count);
                return true;
            }
        }

        // If no buffers available, try more aggressive reclamation with longer waits
        _logger.LogDebug("No buffers available, attempting aggressive reclamation");
        for (int i = 0; i < 5; i++)
        {
            // Progressive delays: give hardware more time on later attempts
            Thread.Sleep(2 + i);
            ReclaimOutputBuffers();
            ProcessCaptureBuffers(); // Also try to drain capture buffers

            lock (_bufferLock)
            {
                if (_availableOutputBuffers.Count > 0)
                {
                    bufferIndex = _availableOutputBuffers.Dequeue();
                    mappedBuffer = _outputBuffers[(int)bufferIndex];
                    _logger.LogDebug("Acquired buffer {Index} after {Attempts} attempts", bufferIndex, i + 1);
                    return true;
                }
            }
        }

        _logger.LogWarning("Failed to acquire any output buffer after multiple attempts");
        bufferIndex = 0;
        mappedBuffer = null!;
        return false;
    }    private void ReturnOutputBuffer(uint index)
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

            var confirmedOutputFormat = GetCurrentPixFormat(V4L2BufferType.VIDEO_OUTPUT_MPLANE);
            _outputPlaneCount = System.Math.Max(1, confirmedOutputFormat.NumPlanes == 0 ? 1 : confirmedOutputFormat.NumPlanes);

            _logger.LogInformation(
                "Set output format: {Width}x{Height} H264 ({Planes} plane(s))",
                confirmedOutputFormat.Width,
                confirmedOutputFormat.Height,
                _outputPlaneCount);

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
            var confirmedCaptureFormat = GetCurrentPixFormat(V4L2BufferType.VIDEO_CAPTURE_MPLANE);
            _capturePlaneCount = System.Math.Max(1, confirmedCaptureFormat.NumPlanes == 0 ? 1 : confirmedCaptureFormat.NumPlanes);

            if (_capturePlaneCount != captureFormat.NumPlanes)
            {
                _logger.LogInformation(
                    "Capture plane count adjusted by driver from {RequestedPlanes} to {ActualPlanes}",
                    captureFormat.NumPlanes,
                    _capturePlaneCount);
            }

            _logger.LogInformation(
                "Set capture format: {Width}x{Height} fmt=0x{Pixel:X8} ({Planes} plane(s))",
                confirmedCaptureFormat.Width,
                confirmedCaptureFormat.Height,
                confirmedCaptureFormat.PixelFormat,
                _capturePlaneCount);

            _logger.LogInformation("Format configuration completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure formats");
            throw new InvalidOperationException($"Format configuration failed: {ex.Message}", ex);
        }
    }

    private V4L2PixFormatMplane GetCurrentPixFormat(V4L2BufferType bufferType)
    {
        var format = new V4L2Format
        {
            Type = bufferType
        };

        var result = LibV4L2.GetFormat(_device.fd, ref format);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to query current format for {bufferType}: {result.ErrorMessage ?? $"errno {result.ErrorCode}"}");
        }

        return format.Pix_mp;
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

    private void InitializeMediaRequests()
    {
        if (_requestApiEnabled)
            return;

        var mediaPath = _configuration.MediaDevicePath;
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            _logger.LogInformation("Media device path not provided; request API disabled.");
            return;
        }

        var fd = Libc.open(mediaPath, OpenFlags.O_RDWR | OpenFlags.O_NONBLOCK);
        if (fd < 0)
        {
            var errno = Marshal.GetLastPInvokeError();
            _logger.LogWarning("Failed to open media device {Path}: errno {Errno}. Continuing without request API.", mediaPath, errno);
            return;
        }

        _mediaFd = fd;
        _requestApiEnabled = true;

        var targetPoolSize = (int)Math.Max(1, _configuration.RequestPoolSize);
        for (int i = 0; i < targetPoolSize; i++)
        {
            if (!TryAllocateMediaRequest())
            {
                if (_availableRequests.Count == 0)
                {
                    _logger.LogWarning("Media request allocation failed; disabling request API.");
                    DisableMediaRequests();
                }
                break;
            }
        }

        if (_requestApiEnabled)
        {
            _logger.LogInformation("Media request API enabled via {Path} ({RequestCount} pre-allocated requests)", mediaPath, _availableRequests.Count);
        }
    }

    private bool TryAllocateMediaRequest()
    {
        if (_mediaFd < 0)
            return false;

        var (result, requestFd) = LibMedia.AllocateRequest(_mediaFd);
        if (!result.Success || requestFd < 0)
        {
            _logger.LogWarning("MEDIA_IOC_REQUEST_ALLOC failed: {Error}", result.ErrorMessage ?? $"errno {result.ErrorCode}");
            return false;
        }

        var request = new MediaRequestContext(requestFd);
        lock (_bufferLock)
        {
            _availableRequests.Enqueue(request);
        }
        return true;
    }

    private bool TryAcquireMediaRequest(out MediaRequestContext request)
    {
        request = null!;

        if (!_requestApiEnabled)
            return false;

        lock (_bufferLock)
        {
            if (_availableRequests.Count > 0)
            {
                request = _availableRequests.Dequeue();
                return true;
            }
        }

        if (TryAllocateMediaRequest())
        {
            lock (_bufferLock)
            {
                if (_availableRequests.Count > 0)
                {
                    request = _availableRequests.Dequeue();
                    return true;
                }
            }
        }

        _logger.LogWarning("No media requests available when required.");
        return false;
    }

    private void ReleaseMediaRequest(MediaRequestContext request)
    {
        if (!_requestApiEnabled)
            return;

        lock (_bufferLock)
        {
            _availableRequests.Enqueue(request.Reset());
        }
    }

    private void DisableMediaRequests()
    {
        if (!_requestApiEnabled)
            return;

        lock (_bufferLock)
        {
            while (_availableRequests.Count > 0)
            {
                var request = _availableRequests.Dequeue();
                Libc.close(request.RequestFd);
            }
        }

        List<MediaRequestContext> inflight;
        lock (_bufferLock)
        {
            inflight = _inFlightRequests.Values.ToList();
            _inFlightRequests.Clear();
        }

        foreach (var entry in inflight)
        {
            Libc.close(entry.RequestFd);
        }
        _requestApiEnabled = false;

        if (_mediaFd >= 0)
        {
            Libc.close(_mediaFd);
            _mediaFd = -1;
        }
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

        // Map each buffer using VIDIOC_QUERYBUF + mmap
        for (uint i = 0; i < reqBufs.Count; i++)
        {
            var configuredPlaneCount = bufferType == V4L2BufferType.VIDEO_OUTPUT_MPLANE
                ? _outputPlaneCount
                : _capturePlaneCount;

            var planeCount = System.Math.Clamp(configuredPlaneCount, 1, (int)V4L2Constants.VIDEO_MAX_PLANES);
            var planes = new V4L2Plane[planeCount];
            var planePointers = new IntPtr[planeCount];
            var planeSizes = new uint[planeCount];

            unsafe
            {
                var planeStorage = stackalloc V4L2Plane[planeCount];
                for (int p = 0; p < planeCount; p++)
                {
                    planeStorage[p] = new V4L2Plane();
                }

                var buffer = new V4L2Buffer
                {
                    Index = i,
                    Type = bufferType,
                    Memory = V4L2Constants.V4L2_MEMORY_MMAP,
                    Length = (uint)planeCount,
                    Field = (uint)V4L2Field.NONE,
                    Planes = planeStorage
                };

                var queryResult = LibV4L2.QueryBuffer(_device.fd, ref buffer);
                if (!queryResult.Success)
                {
                    throw new InvalidOperationException($"Failed to query buffer {i} for {bufferType}: {queryResult.ErrorMessage}");
                }

                for (int p = 0; p < planeCount; p++)
                {
                    planes[p] = planeStorage[p];
                    planeSizes[p] = planeStorage[p].Length;

                    if (planeSizes[p] == 0)
                    {
                        throw new InvalidOperationException($"Queried plane {p} for buffer {i} has zero length");
                    }

                    var offset = planeStorage[p].Memory.MemOffset;
                    var mapped = Libc.mmap(IntPtr.Zero, (nuint)planeSizes[p], ProtFlags.PROT_READ | ProtFlags.PROT_WRITE, MapFlags.MAP_SHARED, _device.fd, (nint)offset);
                    if (mapped == Libc.MAP_FAILED)
                    {
                        var errno = Marshal.GetLastPInvokeError();
                        for (int rollback = 0; rollback < p; rollback++)
                        {
                            if (planePointers[rollback] == IntPtr.Zero)
                                continue;

                            unsafe
                            {
                                Libc.munmap((void*)planePointers[rollback], planeSizes[rollback]);
                            }
                            planePointers[rollback] = IntPtr.Zero;
                        }
                        throw new InvalidOperationException($"mmap failed for buffer {i} plane {p}: errno {errno}");
                    }

                    planePointers[p] = mapped;
                    planes[p].BytesUsed = 0;
                    planes[p].DataOffset = 0;
                }
            }

            var mappedBuffer = new MappedBuffer
            {
                Index = i,
                PlanePointers = planePointers,
                PlaneSizes = planeSizes,
                Planes = planes
            };

            bufferList.Add(mappedBuffer);
            availableQueue.Enqueue(i);

            _logger.LogTrace("Mapped buffer {Index} for {BufferType}: planes={PlaneCount}",
                i, bufferType, planeCount);
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

    private bool VerifyStreamingState()
    {
        try
        {
            // Try to get current format to verify device is responsive
            var outputFormat = GetCurrentPixFormat(V4L2BufferType.VIDEO_OUTPUT_MPLANE);
            var captureFormat = GetCurrentPixFormat(V4L2BufferType.VIDEO_CAPTURE_MPLANE);

            _logger.LogDebug("Streaming verification: Output {OutputFormat:X8}, Capture {CaptureFormat:X8}",
                outputFormat.PixelFormat, captureFormat.PixelFormat);

            return outputFormat.PixelFormat != 0 && captureFormat.PixelFormat != 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify streaming state");
            return false;
        }
    }

    private void ResetDecoderState()
    {
        _logger.LogWarning("Resetting decoder state due to persistent errors");

        try
        {
            // Stop streaming
            _device.StreamOff(V4L2BufferType.VIDEO_OUTPUT_MPLANE);
            _device.StreamOff(V4L2BufferType.VIDEO_CAPTURE_MPLANE);

            // Clear buffer queues
            lock (_bufferLock)
            {
                _availableOutputBuffers.Clear();
                _availableCaptureBuffers.Clear();

                // Re-queue all buffers as available
                for (uint i = 0; i < _outputBuffers.Count; i++)
                {
                    _availableOutputBuffers.Enqueue(i);
                }
                for (uint i = 0; i < _captureBuffers.Count; i++)
                {
                    _availableCaptureBuffers.Enqueue(i);
                }
            }

            // Restart streaming
            StartStreaming();

            _logger.LogInformation("Decoder state reset completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset decoder state");
            throw;
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

            DisableMediaRequests();

            _isInitialized = false;
            _logger.LogInformation("Decoder cleanup completed");
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
            for (int p = 0; p < buffer.PlanePointers.Length; p++)
            {
                var planePtr = buffer.PlanePointers[p];
                if (planePtr == IntPtr.Zero)
                    continue;

                try
                {
                    unsafe
                    {
                        var result = Libc.munmap((void*)planePtr, buffer.PlaneSizes[p]);
                        if (result != 0)
                        {
                            var errno = Marshal.GetLastPInvokeError();
                            _logger.LogWarning("munmap failed for buffer {Index} plane {Plane}: errno {Errno}", buffer.Index, p, errno);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to munmap buffer {Index} plane {Plane}", buffer.Index, p);
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

    private sealed class MediaRequestContext
    {
        public MediaRequestContext(int requestFd)
        {
            RequestFd = requestFd;
        }

        public int RequestFd { get; }
        public uint BufferIndex { get; set; }
        public bool InUse { get; set; }

        public MediaRequestContext Reset()
        {
            BufferIndex = 0;
            InUse = false;
            return this;
        }
    }
}