using Microsoft.Extensions.Logging;
using SharpVideo.Linux.Native;
using SharpVideo.H264;

namespace SharpVideo.V4L2DecodeDemo.Services.Stateless;

/// <summary>
/// Implementation of H.264 parameter set parser for stateless decoders
/// </summary>
public class H264ParameterSetParser
{
    private readonly ILogger<H264ParameterSetParser> _logger;
    private readonly uint _initialWidth;
    private readonly uint _initialHeight;
    private readonly H264NaluParser _naluParserWithStartCode;
    private readonly H264NaluParser _naluParserWithoutStartCode;

    public H264ParameterSetParser(ILogger<H264ParameterSetParser> logger, uint initialWidth = 1920, uint initialHeight = 1080)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _initialWidth = initialWidth;
        _initialHeight = initialHeight;

        // Initialize NALU parsers for both modes
        _naluParserWithStartCode = new H264NaluParser(NaluMode.WithStartCode);
        _naluParserWithoutStartCode = new H264NaluParser(NaluMode.WithoutStartCode);
    }

    /// <inheritdoc />
    public V4L2CtrlH264Pps ParsePpsToControl(ReadOnlySpan<byte> ppsData)
    {
        uint chromaFormatIdc = 1; // 4:2:0 YUV format (most common)
        var parsedPps = H264PpsParser.ParsePps(ppsData, chromaFormatIdc);

        if (parsedPps == null)
        {
            throw new Exception("Not able to parse PPS");
        }

        // Convert the parsed PPS to V4L2 control structure
        var v4l2Pps = ConvertPpsStateToV4L2(parsedPps);

        _logger.LogDebug(
            "Successfully parsed PPS using H264PpsParser: ID={PpsId}, SPS_ID={SpsId}, QP={QP}",
            v4l2Pps.PicParameterSetId, v4l2Pps.SeqParameterSetId, v4l2Pps.PicInitQpMinus26 + 26);

        return v4l2Pps;

    }

    /// <summary>
    /// Parse SPS NALU data into V4L2 control structure using the new parser
    /// </summary>
    public V4L2CtrlH264Sps ParseSps(ReadOnlySpan<byte> spsData)
    {
        var parsedSps = H264SpsParser.ParseSps(spsData);


        //_logger.LogDebug("Parsed SPS: Profile=0x{Profile:X2}, Level=0x{Level:X2}, Constraints=0x{Constraints:X2}, Width={Width}MB, Height={Height}MB",
        //    sps.ProfileIdc, sps.LevelIdc, sps.ConstraintSetFlags, sps.PicWidthInMbsMinus1 + 1, sps.PicHeightInMapUnitsMinus1 + 1);

        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public V4L2CtrlH264SliceParams ParseSliceHeaderToControl(ReadOnlySpan<byte> sliceData)
    {
        // Determine which parser to use based on data format
        var parser = HasStartCode(sliceData) ? _naluParserWithStartCode : _naluParserWithoutStartCode;

        // Validate NALU format
        if (!parser.HasValidFormat(sliceData))
            throw new ArgumentException("Invalid slice NALU data format");

        // Extract NALU type and validate
        var naluType = parser.GetNaluType(sliceData);
        if (!IsFrameNalu((byte)naluType))
            throw new ArgumentException($"Expected slice NALU, got type {(int)naluType}");

        // Get the raw NALU payload for bitstream parsing
        var naluPayload = parser.GetNaluPayload(sliceData);

        try
        {
            return ParseSliceHeaderFromBitstream(naluPayload.ToArray(), 0, (byte)naluType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse slice header, using fallback");
            return CreateFallbackSliceParams((byte)naluType);
        }
    }

    /// <inheritdoc />
    public int GetNaluHeaderPosition(byte[] naluData, bool useStartCodes)
    {
        // Use the new parser API instead of manual detection
        var parser = useStartCodes ? _naluParserWithStartCode : _naluParserWithoutStartCode;

        if (!parser.HasValidFormat(naluData))
            return naluData.Length; // Invalid format

        var payload = parser.GetNaluPayload(naluData);
        return naluData.Length - payload.Length; // Offset to payload = start code length
    }


    /// <summary>
    /// Checks if the NALU data has a start code using the new parser
    /// </summary>
    private static bool HasStartCode(ReadOnlySpan<byte> naluData)
    {
        if (naluData.Length >= 4 &&
            naluData[0] == 0x00 && naluData[1] == 0x00 &&
            naluData[2] == 0x00 && naluData[3] == 0x01)
        {
            return true;
        }

        if (naluData.Length >= 3 &&
            naluData[0] == 0x00 && naluData[1] == 0x00 && naluData[2] == 0x01)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Convert PpsState from H264PpsParser to V4L2CtrlH264Pps structure
    /// </summary>
    private V4L2CtrlH264Pps ConvertPpsStateToV4L2(PpsState pps)
    {
        var v4l2Pps = new V4L2CtrlH264Pps
        {
            PicParameterSetId = (byte)Math.Min(pps.pic_parameter_set_id, 255),
            SeqParameterSetId = (byte)Math.Min(pps.seq_parameter_set_id, 31),
            NumSliceGroupsMinus1 = (byte)Math.Min(pps.num_slice_groups_minus1, 7),
            NumRefIdxL0DefaultActiveMinus1 = (byte)Math.Min(pps.num_ref_idx_l0_default_active_minus1, 31),
            NumRefIdxL1DefaultActiveMinus1 = (byte)Math.Min(pps.num_ref_idx_l1_default_active_minus1, 31),
            WeightedBipredIdc = (byte)Math.Min(pps.weighted_bipred_idc, 3),
            PicInitQpMinus26 = (sbyte)Math.Max(-26, Math.Min(pps.pic_init_qp_minus26, 25)),
            PicInitQsMinus26 = (sbyte)Math.Max(-26, Math.Min(pps.pic_init_qs_minus26, 25)),
            ChromaQpIndexOffset = (sbyte)Math.Max(-12, Math.Min(pps.chroma_qp_index_offset, 12)),
            SecondChromaQpIndexOffset = (sbyte)Math.Max(-12, Math.Min(pps.second_chroma_qp_index_offset, 12)),
            Flags = 0
        };

        // Set flags based on parsed PPS values
        if (pps.entropy_coding_mode_flag != 0)
            v4l2Pps.Flags |= 0x01; // V4L2_H264_PPS_FLAG_ENTROPY_CODING_MODE

        if (pps.bottom_field_pic_order_in_frame_present_flag != 0)
            v4l2Pps.Flags |= 0x02; // V4L2_H264_PPS_FLAG_BOTTOM_FIELD_PIC_ORDER_IN_FRAME_PRESENT

        if (pps.weighted_pred_flag != 0)
            v4l2Pps.Flags |= 0x04; // V4L2_H264_PPS_FLAG_WEIGHTED_PRED

        if (pps.deblocking_filter_control_present_flag != 0)
            v4l2Pps.Flags |= 0x08; // V4L2_H264_PPS_FLAG_DEBLOCKING_FILTER_CONTROL_PRESENT

        if (pps.constrained_intra_pred_flag != 0)
            v4l2Pps.Flags |= 0x10; // V4L2_H264_PPS_FLAG_CONSTRAINED_INTRA_PRED

        if (pps.redundant_pic_cnt_present_flag != 0)
            v4l2Pps.Flags |= 0x20; // V4L2_H264_PPS_FLAG_REDUNDANT_PIC_CNT_PRESENT

        if (pps.transform_8x8_mode_flag != 0)
            v4l2Pps.Flags |= 0x40; // V4L2_H264_PPS_FLAG_TRANSFORM_8X8_MODE

        if (pps.pic_scaling_matrix_present_flag != 0)
            v4l2Pps.Flags |= 0x80; // V4L2_H264_PPS_FLAG_PIC_SCALING_MATRIX_PRESENT

        return v4l2Pps;
    }

    /// <summary>
    /// Parse slice header from bitstream
    /// </summary>
    private V4L2CtrlH264SliceParams ParseSliceHeaderFromBitstream(ReadOnlyMemory<byte> sliceData, int naluStart, byte naluType)
    {
        var reader = new H264BitstreamReader(sliceData);

        var sliceParams = new V4L2CtrlH264SliceParams
        {
            FirstMbInSlice = (uint)reader.ReadUEG(),
            SliceType = MapH264SliceType(naluType),
            ColourPlaneId = 0,
            RedundantPicCnt = 0,
            CabacInitIdc = 0,
            SliceQpDelta = 0,
            SliceQsDelta = 0,
            DisableDeblockingFilterIdc = 0,
            SliceAlphaC0OffsetDiv2 = 0,
            SliceBetaOffsetDiv2 = 0,
            NumRefIdxL0ActiveMinus1 = 0,
            NumRefIdxL1ActiveMinus1 = 0,
            Flags = 0
        };

        var sliceTypeFromHeader = (byte)reader.ReadUEG();
        sliceParams.SliceType = (byte)(sliceTypeFromHeader % 5); // Map slice type

        var picParameterSetId = reader.ReadUEG();

        // Would continue parsing slice header parameters...
        // For now, use minimal parsing with safe defaults

        sliceParams.HeaderBitSize = (uint)((reader.Position + 7) / 8 * 8); // Round up to byte boundary

        return sliceParams;
    }

    /// <summary>
    /// Create fallback slice parameters
    /// </summary>
    private V4L2CtrlH264SliceParams CreateFallbackSliceParams(byte naluType)
    {
        return new V4L2CtrlH264SliceParams
        {
            HeaderBitSize = 32, // Minimal header size
            FirstMbInSlice = 0,
            SliceType = MapH264SliceType(naluType),
            ColourPlaneId = 0,
            RedundantPicCnt = 0,
            CabacInitIdc = 0,
            SliceQpDelta = 0,
            SliceQsDelta = 0,
            DisableDeblockingFilterIdc = 0,
            SliceAlphaC0OffsetDiv2 = 0,
            SliceBetaOffsetDiv2 = 0,
            NumRefIdxL0ActiveMinus1 = 0,
            NumRefIdxL1ActiveMinus1 = 0,
            Flags = 0
        };
    }

    /// <summary>
    /// Determines if a NALU type represents frame data
    /// </summary>
    private static bool IsFrameNalu(byte naluType)
    {
        return naluType switch
        {
            1 => true,  // Non-IDR slice
            5 => true,  // IDR slice
            2 => true,  // Data Partition A
            3 => true,  // Data Partition B
            4 => true,  // Data Partition C
            _ => false
        };
    }

    /// <summary>
    /// Maps H.264 NALU type to V4L2 slice type (corrected logic)
    /// </summary>
    private static byte MapH264SliceType(byte naluType)
    {
        return naluType switch
        {
            1 => 0, // Non-IDR slice -> P slice (most common for type 1)
            5 => 2, // IDR slice -> I slice
            _ => 0  // Default to P slice
        };
    }
}