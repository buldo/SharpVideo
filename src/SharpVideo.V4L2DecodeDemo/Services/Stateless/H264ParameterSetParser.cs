using Microsoft.Extensions.Logging;
using SharpVideo.Linux.Native;
using SharpVideo.H264;
using SharpVideo.V4L2DecodeDemo.Interfaces;

namespace SharpVideo.V4L2DecodeDemo.Services.Stateless;

/// <summary>
/// Implementation of H.264 parameter set parser for stateless decoders
/// </summary>
public class H264ParameterSetParser : IH264ParameterSetParser
{
    private readonly ILogger<H264ParameterSetParser> _logger;
    private readonly uint _initialWidth;
    private readonly uint _initialHeight;

    public H264ParameterSetParser(ILogger<H264ParameterSetParser> logger, uint initialWidth = 1920, uint initialHeight = 1080)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _initialWidth = initialWidth;
        _initialHeight = initialHeight;
    }

    /// <inheritdoc />
    public V4L2CtrlH264Sps ParseSpsToControl(byte[] spsData)
    {
        // Remove start code to get raw NALU
        int naluStart = GetNaluHeaderPosition(spsData, true);
        if (naluStart >= spsData.Length)
            throw new ArgumentException("Invalid SPS NALU data");

        var naluHeader = spsData[naluStart];
        var naluType = (byte)(naluHeader & 0x1F);

        if (naluType != 7)
            throw new ArgumentException($"Expected SPS NALU (type 7), got type {naluType}");

        // Extract basic SPS parameters
        var profileIdc = spsData.Length > naluStart + 1 ? spsData[naluStart + 1] : (byte)0;
        var constraintSetFlags = spsData.Length > naluStart + 2 ? spsData[naluStart + 2] : (byte)0;
        var levelIdc = spsData.Length > naluStart + 3 ? spsData[naluStart + 3] : (byte)0;

        // Validate basic parameters
        if (profileIdc == 0 || levelIdc == 0)
        {
            throw new ArgumentException("Invalid SPS: missing profile or level information");
        }

        // Create SPS structure with validated parameters
        var sps = new V4L2CtrlH264Sps
        {
            ProfileIdc = profileIdc,
            ConstraintSetFlags = constraintSetFlags,
            LevelIdc = levelIdc,
            SeqParameterSetId = 0, // For simplicity, assume SPS ID 0
            ChromaFormatIdc = 1, // 4:2:0 YUV format (most common)
            BitDepthLumaMinus8 = 0, // 8-bit luma (8 - 8 = 0)
            BitDepthChromaMinus8 = 0, // 8-bit chroma (8 - 8 = 0)
            Log2MaxFrameNumMinus4 = 0, // log2_max_frame_num_minus4 = 0 means max_frame_num = 16
            PicOrderCntType = 0, // Picture order count type 0 (most common)
            Log2MaxPicOrderCntLsbMinus4 = 0, // For POC type 0
            MaxNumRefFrames = 1, // Single reference frame (conservative)
            NumRefFramesInPicOrderCntCycle = 0, // Not used for POC type 0
            OffsetForRefFrame0 = 0, // Only used for POC type 1
            OffsetForRefFrame1 = 0, // Only used for POC type 1
            OffsetForTopToBottomField = 0, // Only used for POC type 1
            OffsetForNonRefPic = 0, // Only used for POC type 1
            PicWidthInMbsMinus1 = (ushort)((_initialWidth / 16) - 1),
            PicHeightInMapUnitsMinus1 = (ushort)((_initialHeight / 16) - 1),
            Flags = 0 // Additional flags can be set here if needed
        };

        // Validate calculated dimensions
        if (sps.PicWidthInMbsMinus1 == 0xFFFF || sps.PicHeightInMapUnitsMinus1 == 0xFFFF)
        {
            throw new ArgumentException($"Invalid picture dimensions: {_initialWidth}x{_initialHeight}");
        }

        _logger.LogDebug("Parsed SPS: Profile=0x{Profile:X2}, Level=0x{Level:X2}, Constraints=0x{Constraints:X2}, Width={Width}MB, Height={Height}MB",
            sps.ProfileIdc, sps.LevelIdc, sps.ConstraintSetFlags, sps.PicWidthInMbsMinus1 + 1, sps.PicHeightInMapUnitsMinus1 + 1);

        return sps;
    }

    /// <inheritdoc />
    public V4L2CtrlH264Pps ParsePpsToControl(byte[] ppsData)
    {
        // Remove start code to get raw NALU
        int naluStart = GetNaluHeaderPosition(ppsData, true);
        if (naluStart >= ppsData.Length)
            throw new ArgumentException("Invalid PPS NALU data");

        var naluHeader = ppsData[naluStart];
        var naluType = (byte)(naluHeader & 0x1F);

        if (naluType != 8)
            throw new ArgumentException($"Expected PPS NALU (type 8), got type {naluType}");

        // Validate minimum PPS size (should be at least 2 bytes after NALU header)
        if (ppsData.Length < naluStart + 2)
        {
            throw new ArgumentException("PPS NALU too short");
        }

        // Create PPS structure with safe defaults
        var pps = new V4L2CtrlH264Pps
        {
            PicParameterSetId = 0, // Assume PPS ID 0 for simplicity
            SeqParameterSetId = 0, // Links to SPS ID 0
            NumSliceGroupsMinus1 = 0, // Single slice group (most common)
            NumRefIdxL0DefaultActiveMinus1 = 0, // Default: 1 reference frame for L0
            NumRefIdxL1DefaultActiveMinus1 = 0, // Default: 1 reference frame for L1 (not used in P-frames)
            WeightedBipredIdc = 0, // No weighted prediction (simpler)
            PicInitQpMinus26 = 0, // Default QP = 26 (0 + 26)
            PicInitQsMinus26 = 0, // Default QS = 26 (0 + 26)
            ChromaQpIndexOffset = 0, // No chroma QP offset
            SecondChromaQpIndexOffset = 0, // No second chroma QP offset
            Flags = 0 // No special flags
        };

        // Validate PPS parameters
        if (pps.PicInitQpMinus26 < -26 || pps.PicInitQpMinus26 > 25)
        {
            _logger.LogWarning("PPS QP value may be out of range: {QP}", pps.PicInitQpMinus26 + 26);
        }

        _logger.LogDebug("Parsed PPS: ID={PpsId}, SPS_ID={SpsId}, SliceGroups={SliceGroups}, QP={QP}",
            pps.PicParameterSetId, pps.SeqParameterSetId, pps.NumSliceGroupsMinus1 + 1, pps.PicInitQpMinus26 + 26);

        return pps;
    }

    /// <inheritdoc />
    public V4L2CtrlH264SliceParams ParseSliceHeaderToControl(byte[] sliceData, byte sliceType)
    {
        // Remove start code to get raw NALU
        int naluStart = GetNaluHeaderPosition(sliceData, true);
        if (naluStart >= sliceData.Length)
            throw new ArgumentException("Invalid slice NALU data");

        var naluHeader = sliceData[naluStart];
        var naluTypeFromHeader = (byte)(naluHeader & 0x1F);

        if (!IsFrameNalu(naluTypeFromHeader))
            throw new ArgumentException($"Expected slice NALU, got type {naluTypeFromHeader}");

        // For now, create a basic slice params structure
        // In a complete implementation, this would parse the slice header bitstream
        var sliceParams = new V4L2CtrlH264SliceParams
        {
            HeaderBitSize = 32, // Simplified - would need actual bit parsing
            FirstMbInSlice = 0, // First macroblock in slice
            SliceType = MapH264SliceType(sliceType),
            ColourPlaneId = 0, // Not used in 4:2:0
            RedundantPicCnt = 0, // No redundancy
            CabacInitIdc = 0, // CABAC initialization
            SliceQpDelta = 0, // No QP delta
            SliceQsDelta = 0, // No QS delta
            DisableDeblockingFilterIdc = 0, // Enable deblocking
            SliceAlphaC0OffsetDiv2 = 0, // No alpha offset
            SliceBetaOffsetDiv2 = 0, // No beta offset
            NumRefIdxL0ActiveMinus1 = 0, // Single reference
            NumRefIdxL1ActiveMinus1 = 0, // Single reference
            Flags = 0 // Would need to parse specific flags
        };

        _logger.LogDebug("Parsed slice header: Type={SliceType}, FirstMB={FirstMb}, QpDelta={QpDelta}",
            sliceParams.SliceType, sliceParams.FirstMbInSlice, sliceParams.SliceQpDelta);

        return sliceParams;
    }

    /// <inheritdoc />
    public int GetNaluHeaderPosition(byte[] naluData, bool useStartCodes)
    {
        if (useStartCodes)
        {
            // Check for 4-byte start code
            if (naluData.Length >= 4 &&
                naluData[0] == 0x00 && naluData[1] == 0x00 &&
                naluData[2] == 0x00 && naluData[3] == 0x01)
            {
                return 4;
            }

            // Check for 3-byte start code
            if (naluData.Length >= 3 &&
                naluData[0] == 0x00 && naluData[1] == 0x00 && naluData[2] == 0x01)
            {
                return 3;
            }
        }

        // No start code found or not using start codes, assume data starts immediately
        return 0;
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
    /// Maps H.264 slice type to V4L2 slice type
    /// </summary>
    private static byte MapH264SliceType(byte h264SliceType)
    {
        return h264SliceType switch
        {
            1 => 0, // Non-IDR slice -> P slice
            5 => 1, // IDR slice -> I slice
            _ => 0  // Default to P slice
        };
    }

    /// <summary>
    /// Extract SPS from H.264 bitstream file
    /// </summary>
    public async Task<V4L2CtrlH264Sps?> ExtractSpsAsync(string filePath)
    {
        try
        {
            using var fileStream = File.OpenRead(filePath);
            using var naluProvider = new H264NaluProvider(NaluOutputMode.WithoutStartCode);

            // Read file data
            int maxBytes = Math.Min(1024 * 1024, (int)fileStream.Length);
            var buffer = new byte[maxBytes];
            int bytesRead = await fileStream.ReadAsync(buffer, 0, maxBytes);

            // Feed data to NALU provider
            await naluProvider.AppendData(buffer.AsSpan(0, bytesRead).ToArray(), CancellationToken.None);
            naluProvider.CompleteWriting();

            // Read NALUs and look for SPS
            await foreach (var naluData in naluProvider.NaluReader.ReadAllAsync(CancellationToken.None))
            {
                if (naluData.Length < 1) continue;

                byte naluType = (byte)(naluData[0] & 0x1F);
                if (naluType == 7) // SPS NALU type
                {
                    return ParseSps(naluData.AsSpan());
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract SPS from file: {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Extract PPS from H.264 bitstream file
    /// </summary>
    public async Task<V4L2CtrlH264Pps?> ExtractPpsAsync(string filePath)
    {
        try
        {
            using var fileStream = File.OpenRead(filePath);
            using var naluProvider = new H264NaluProvider(NaluOutputMode.WithoutStartCode);

            // Read file data
            int maxBytes = Math.Min(1024 * 1024, (int)fileStream.Length);
            var buffer = new byte[maxBytes];
            int bytesRead = await fileStream.ReadAsync(buffer, 0, maxBytes);

            // Feed data to NALU provider
            await naluProvider.AppendData(buffer.AsSpan(0, bytesRead).ToArray(), CancellationToken.None);
            naluProvider.CompleteWriting();

            // Read NALUs and look for PPS
            await foreach (var naluData in naluProvider.NaluReader.ReadAllAsync(CancellationToken.None))
            {
                if (naluData.Length < 1) continue;

                byte naluType = (byte)(naluData[0] & 0x1F);
                if (naluType == 8) // PPS NALU type
                {
                    return ParsePps(naluData.AsSpan());
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract PPS from file: {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Parse SPS NALU data into V4L2 control structure
    /// </summary>
    public V4L2CtrlH264Sps ParseSps(ReadOnlySpan<byte> spsData)
    {
        // For now, delegate to existing method
        return ParseSpsToControl(spsData.ToArray());
    }

    /// <summary>
    /// Parse PPS NALU data into V4L2 control structure
    /// </summary>
    public V4L2CtrlH264Pps ParsePps(ReadOnlySpan<byte> ppsData)
    {
        // For now, delegate to existing method
        return ParsePpsToControl(ppsData.ToArray());
    }

    /// <summary>
    /// Parse slice header to create V4L2 slice parameters
    /// </summary>
    public V4L2CtrlH264SliceParams ParseSliceHeader(ReadOnlySpan<byte> sliceData, V4L2CtrlH264Sps sps, V4L2CtrlH264Pps pps)
    {
        // For now, delegate to existing method
        return ParseSliceHeaderToControl(sliceData.ToArray(), 0); // Using frame number 0 for simplicity
    }

    /// <summary>
    /// Parse slice header to create V4L2 slice parameters (legacy method)
    /// </summary>
    public V4L2CtrlH264SliceParams ParseSliceHeaderToControl(byte[] sliceData, uint frameNum)
    {
        // Convert to byte and delegate to existing method
        return ParseSliceHeaderToControl(sliceData, (byte)frameNum);
    }
}