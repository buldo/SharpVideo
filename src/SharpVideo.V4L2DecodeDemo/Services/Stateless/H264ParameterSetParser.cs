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
    public V4L2CtrlH264Sps ParseSpsToControl(byte[] spsData)
    {
        // Determine which parser to use based on data format
        var parser = HasStartCode(spsData) ? _naluParserWithStartCode : _naluParserWithoutStartCode;

        // Validate NALU format
        if (!parser.HasValidFormat(spsData))
            throw new ArgumentException("Invalid SPS NALU data format");

        // Extract NALU type and validate
        var naluType = parser.GetNaluType(spsData);
        if (naluType != H264NaluType.SequenceParameterSet)
            throw new ArgumentException($"Expected SPS NALU (type 7), got type {(int)naluType}");

        // Get the raw NALU payload for bitstream parsing
        var naluPayload = parser.GetNaluPayload(spsData);

        // Parse SPS bitstream for real parameters
        try
        {
            var sps = ParseSpsFromBitstream(naluPayload.ToArray(), 0); // Start from header since payload excludes start code

            _logger.LogDebug("Parsed SPS: Profile=0x{Profile:X2}, Level=0x{Level:X2}, Constraints=0x{Constraints:X2}, Width={Width}MB, Height={Height}MB",
                sps.ProfileIdc, sps.LevelIdc, sps.ConstraintSetFlags, sps.PicWidthInMbsMinus1 + 1, sps.PicHeightInMapUnitsMinus1 + 1);

            return sps;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse SPS bitstream, using fallback values");
            return CreateFallbackSps(naluPayload.ToArray(), 0);
        }
    }

    /// <inheritdoc />
    public V4L2CtrlH264Pps ParsePpsToControl(byte[] ppsData)
    {
        // Determine which parser to use based on data format
        var parser = HasStartCode(ppsData) ? _naluParserWithStartCode : _naluParserWithoutStartCode;

        // Validate NALU format
        if (!parser.HasValidFormat(ppsData))
            throw new ArgumentException("Invalid PPS NALU data format");

        // Extract NALU type and validate
        var naluType = parser.GetNaluType(ppsData);
        if (naluType != H264NaluType.PictureParameterSet)
            throw new ArgumentException($"Expected PPS NALU (type 8), got type {(int)naluType}");

        // Get the raw NALU payload for bitstream parsing
        var naluPayload = parser.GetNaluPayload(ppsData);

        try
        {
            var pps = ParsePpsFromBitstream(naluPayload.ToArray(), 0); // Start from header since payload excludes start code

            _logger.LogDebug("Parsed PPS: ID={PpsId}, SPS_ID={SpsId}, SliceGroups={SliceGroups}, QP={QP}",
                pps.PicParameterSetId, pps.SeqParameterSetId, pps.NumSliceGroupsMinus1 + 1, pps.PicInitQpMinus26 + 26);

            return pps;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse PPS bitstream, using fallback values");
            return CreateFallbackPps();
        }
    }

    /// <inheritdoc />
    public V4L2CtrlH264SliceParams ParseSliceHeaderToControl(byte[] sliceData)
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
    /// Parse SPS NALU data into V4L2 control structure using the new parser
    /// </summary>
    public V4L2CtrlH264Sps ParseSps(ReadOnlySpan<byte> spsData)
    {
        return ParseSpsToControl(spsData.ToArray());
    }

    /// <summary>
    /// Parse PPS NALU data into V4L2 control structure using the new parser
    /// </summary>
    public V4L2CtrlH264Pps ParsePps(ReadOnlySpan<byte> ppsData)
    {
        return ParsePpsToControl(ppsData.ToArray());
    }

    /// <summary>
    /// Parse slice header to create V4L2 slice parameters (legacy method)
    /// </summary>
    public V4L2CtrlH264SliceParams ParseSliceHeaderToControl(byte[] sliceData, uint frameNum)
    {
        return ParseSliceHeaderToControl(sliceData, (byte)frameNum);
    }

    /// <summary>
    /// Checks if the NALU data has a start code using the new parser
    /// </summary>
    private static bool HasStartCode(byte[] naluData)
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
    /// Parse SPS from actual H.264 bitstream
    /// </summary>
    private V4L2CtrlH264Sps ParseSpsFromBitstream(byte[] spsData, int naluStart)
    {
        var reader = new H264BitstreamReader(spsData, naluStart + 1); // Skip NALU header

        // Parse basic SPS parameters
        var profileIdc = reader.ReadByte();
        var constraintSetFlags = reader.ReadByte();
        var levelIdc = reader.ReadByte();
        var seqParameterSetId = reader.ReadUEG(); // UE(v)

        // Initialize SPS structure
        var sps = new V4L2CtrlH264Sps
        {
            ProfileIdc = profileIdc,
            ConstraintSetFlags = constraintSetFlags,
            LevelIdc = levelIdc,
            SeqParameterSetId = (byte)seqParameterSetId,
            OffsetForRefFrame = new int[255] // Initialize array
        };

        // Parse chroma format
        if (profileIdc == 100 || profileIdc == 110 || profileIdc == 122 || profileIdc == 244 ||
            profileIdc == 44 || profileIdc == 83 || profileIdc == 86 || profileIdc == 118 ||
            profileIdc == 128 || profileIdc == 138 || profileIdc == 139 || profileIdc == 134)
        {
            sps.ChromaFormatIdc = (byte)reader.ReadUEG();

            if (sps.ChromaFormatIdc == 3)
            {
                reader.ReadBit(); // separate_colour_plane_flag
            }

            sps.BitDepthLumaMinus8 = (byte)reader.ReadUEG();
            sps.BitDepthChromaMinus8 = (byte)reader.ReadUEG();

            reader.ReadBit(); // qpprime_y_zero_transform_bypass_flag

            var seqScalingMatrixPresentFlag = reader.ReadBit();
            if (seqScalingMatrixPresentFlag)
            {
                // Skip scaling matrix parsing for now
                for (int i = 0; i < ((sps.ChromaFormatIdc != 3) ? 8 : 12); i++)
                {
                    if (reader.ReadBit()) // seq_scaling_list_present_flag[i]
                    {
                        // Skip scaling list
                        var sizeOfScalingList = (i < 6) ? 16 : 64;
                        for (int j = 0; j < sizeOfScalingList; j++)
                        {
                            reader.ReadSEG(); // Skip scaling list values
                        }
                    }
                }
            }
        }
        else
        {
            sps.ChromaFormatIdc = 1; // 4:2:0 default
            sps.BitDepthLumaMinus8 = 0;
            sps.BitDepthChromaMinus8 = 0;
        }

        // Parse frame numbering parameters
        sps.Log2MaxFrameNumMinus4 = (byte)reader.ReadUEG();
        sps.PicOrderCntType = (byte)reader.ReadUEG();

        if (sps.PicOrderCntType == 0)
        {
            sps.Log2MaxPicOrderCntLsbMinus4 = (byte)reader.ReadUEG();
        }
        else if (sps.PicOrderCntType == 1)
        {
            reader.ReadBit(); // delta_pic_order_always_zero_flag
            sps.OffsetForNonRefPic = reader.ReadSEG();
            sps.OffsetForTopToBottomField = reader.ReadSEG();
            sps.NumRefFramesInPicOrderCntCycle = (byte)reader.ReadUEG();

            for (int i = 0; i < sps.NumRefFramesInPicOrderCntCycle && i < 255; i++)
            {
                sps.OffsetForRefFrame[i] = reader.ReadSEG();
            }
        }

        sps.MaxNumRefFrames = (byte)reader.ReadUEG();
        reader.ReadBit(); // gaps_in_frame_num_value_allowed_flag

        // Parse picture dimensions
        var picWidthInMbsMinus1 = reader.ReadUEG();
        var picHeightInMapUnitsMinus1 = reader.ReadUEG();

        sps.PicWidthInMbsMinus1 = (ushort)picWidthInMbsMinus1;
        sps.PicHeightInMapUnitsMinus1 = (ushort)picHeightInMapUnitsMinus1;

        var frameMbsOnlyFlag = reader.ReadBit();
        if (!frameMbsOnlyFlag)
        {
            reader.ReadBit(); // mb_adaptive_frame_field_flag
        }

        reader.ReadBit(); // direct_8x8_inference_flag

        var frameCroppingFlag = reader.ReadBit();
        if (frameCroppingFlag)
        {
            reader.ReadUEG(); // frame_crop_left_offset
            reader.ReadUEG(); // frame_crop_right_offset
            reader.ReadUEG(); // frame_crop_top_offset
            reader.ReadUEG(); // frame_crop_bottom_offset
        }

        // Set flags based on parsed values
        sps.Flags = 0;
        if (frameMbsOnlyFlag) sps.Flags |= 0x01; // V4L2_H264_SPS_FLAG_FRAME_MBS_ONLY

        return sps;
    }

    /// <summary>
    /// Create fallback SPS when bitstream parsing fails
    /// </summary>
    private V4L2CtrlH264Sps CreateFallbackSps(byte[] spsData, int naluStart)
    {
        // Extract basic parameters safely
        var profileIdc = spsData.Length > naluStart + 1 ? spsData[naluStart + 1] : (byte)100; // High profile default
        var constraintSetFlags = spsData.Length > naluStart + 2 ? spsData[naluStart + 2] : (byte)0;
        var levelIdc = spsData.Length > naluStart + 3 ? spsData[naluStart + 3] : (byte)40; // Level 4.0 default

        // Validate basic parameters
        if (profileIdc == 0) profileIdc = 100;
        if (levelIdc == 0) levelIdc = 40;

        // Create SPS structure with safe defaults
        var sps = new V4L2CtrlH264Sps
        {
            ProfileIdc = profileIdc,
            ConstraintSetFlags = constraintSetFlags,
            LevelIdc = levelIdc,
            SeqParameterSetId = 0,
            ChromaFormatIdc = 1, // 4:2:0 YUV format
            BitDepthLumaMinus8 = 0, // 8-bit
            BitDepthChromaMinus8 = 0, // 8-bit
            Log2MaxFrameNumMinus4 = 0,
            PicOrderCntType = 0,
            Log2MaxPicOrderCntLsbMinus4 = 0,
            MaxNumRefFrames = 4, // More reasonable default
            NumRefFramesInPicOrderCntCycle = 0,
            OffsetForRefFrame = new int[255],
            OffsetForNonRefPic = 0,
            OffsetForTopToBottomField = 0,
            PicWidthInMbsMinus1 = (ushort)((_initialWidth / 16) - 1),
            PicHeightInMapUnitsMinus1 = (ushort)((_initialHeight / 16) - 1),
            Flags = 0x01 // Frame MBs only
        };

        return sps;
    }

    /// <summary>
    /// Parse PPS from actual H.264 bitstream
    /// </summary>
    private V4L2CtrlH264Pps ParsePpsFromBitstream(byte[] ppsData, int naluStart)
    {
        var reader = new H264BitstreamReader(ppsData, naluStart + 1); // Skip NALU header

        var pps = new V4L2CtrlH264Pps
        {
            PicParameterSetId = (byte)reader.ReadUEG(),
            SeqParameterSetId = (byte)reader.ReadUEG(),
            Flags = 0
        };

        // Check if we have enough data to continue parsing
        if (!reader.HasMoreData())
        {
            _logger.LogWarning("PPS data too short, using defaults for remaining fields");
            return CreateFallbackPps();
        }

        var entropyCodingModeFlag = reader.ReadBit();
        if (entropyCodingModeFlag) pps.Flags |= 0x01; // V4L2_H264_PPS_FLAG_ENTROPY_CODING_MODE

        if (!reader.HasMoreData()) return pps;
        var bottomFieldPicOrderInFramePresentFlag = reader.ReadBit();
        if (bottomFieldPicOrderInFramePresentFlag) pps.Flags |= 0x02;

        if (!reader.HasMoreData()) return pps;
        pps.NumSliceGroupsMinus1 = (byte)reader.ReadUEG();

        if (pps.NumSliceGroupsMinus1 > 0)
        {
            if (!reader.HasMoreData()) return pps;
            // Skip slice group parsing for now
            var sliceGroupMapType = reader.ReadUEG();
            // Would need to parse slice group parameters based on map type
            // For now, skip this complex parsing
        }

        if (!reader.HasMoreData()) return pps;
        pps.NumRefIdxL0DefaultActiveMinus1 = (byte)reader.ReadUEG();

        if (!reader.HasMoreData()) return pps;
        pps.NumRefIdxL1DefaultActiveMinus1 = (byte)reader.ReadUEG();

        if (!reader.HasMoreData()) return pps;
        var weightedPredFlag = reader.ReadBit();
        if (weightedPredFlag) pps.Flags |= 0x04;

        if (!reader.HasMoreData()) return pps;
        pps.WeightedBipredIdc = (byte)reader.ReadBits(2);

        if (!reader.HasMoreData()) return pps;
        pps.PicInitQpMinus26 = (sbyte)reader.ReadSEG();

        if (!reader.HasMoreData()) return pps;
        pps.PicInitQsMinus26 = (sbyte)reader.ReadSEG();

        if (!reader.HasMoreData()) return pps;
        pps.ChromaQpIndexOffset = (sbyte)reader.ReadSEG();

        if (!reader.HasMoreData()) return pps;
        var deblockingFilterControlPresentFlag = reader.ReadBit();
        if (deblockingFilterControlPresentFlag) pps.Flags |= 0x08;

        if (!reader.HasMoreData()) return pps;
        var constrainedIntraPredFlag = reader.ReadBit();
        if (constrainedIntraPredFlag) pps.Flags |= 0x10;

        if (!reader.HasMoreData()) return pps;
        var redundantPicCntPresentFlag = reader.ReadBit();
        if (redundantPicCntPresentFlag) pps.Flags |= 0x20;

        // Check for more data (transform_8x8_mode_flag, etc.)
        if (reader.HasMoreData())
        {
            var transform8x8ModeFlag = reader.ReadBit();
            if (transform8x8ModeFlag) pps.Flags |= 0x40;

            if (reader.HasMoreData())
            {
                var picScalingMatrixPresentFlag = reader.ReadBit();
                if (picScalingMatrixPresentFlag)
                {
                    // Skip scaling matrix parsing
                }

                if (reader.HasMoreData())
                {
                    pps.SecondChromaQpIndexOffset = (sbyte)reader.ReadSEG();
                }
                else
                {
                    pps.SecondChromaQpIndexOffset = pps.ChromaQpIndexOffset;
                }
            }
            else
            {
                pps.SecondChromaQpIndexOffset = pps.ChromaQpIndexOffset;
            }
        }
        else
        {
            pps.SecondChromaQpIndexOffset = pps.ChromaQpIndexOffset;
        }

        return pps;
    }

    /// <summary>
    /// Create fallback PPS when bitstream parsing fails
    /// </summary>
    private V4L2CtrlH264Pps CreateFallbackPps()
    {
        return new V4L2CtrlH264Pps
        {
            PicParameterSetId = 0,
            SeqParameterSetId = 0,
            NumSliceGroupsMinus1 = 0,
            NumRefIdxL0DefaultActiveMinus1 = 0,
            NumRefIdxL1DefaultActiveMinus1 = 0,
            WeightedBipredIdc = 0,
            PicInitQpMinus26 = 0,
            PicInitQsMinus26 = 0,
            ChromaQpIndexOffset = 0,
            SecondChromaQpIndexOffset = 0,
            Flags = 0x08 // Enable deblocking filter control
        };
    }

    /// <summary>
    /// Parse slice header from bitstream
    /// </summary>
    private V4L2CtrlH264SliceParams ParseSliceHeaderFromBitstream(byte[] sliceData, int naluStart, byte naluType)
    {
        var reader = new H264BitstreamReader(sliceData, naluStart + 1);

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