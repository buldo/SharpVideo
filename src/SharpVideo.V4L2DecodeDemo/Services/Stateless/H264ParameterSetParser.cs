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

        // For now, create a basic SPS structure
        // In a complete implementation, this would parse the bitstream fully
        var sps = new V4L2CtrlH264Sps
        {
            ProfileIdc = spsData.Length > naluStart + 1 ? spsData[naluStart + 1] : (byte)0,
            LevelIdc = spsData.Length > naluStart + 3 ? spsData[naluStart + 3] : (byte)0,
            SeqParameterSetId = 0, // Simplified - would need bitstream parsing
            ChromaFormatIdc = 1, // 4:2:0 default
            BitDepthLumaMinus8 = 0, // 8-bit default
            BitDepthChromaMinus8 = 0, // 8-bit default
            Log2MaxFrameNumMinus4 = 0, // Would need parsing
            PicOrderCntType = 0, // Would need parsing
            Log2MaxPicOrderCntLsbMinus4 = 0,
            MaxNumRefFrames = 1, // Conservative default
            NumRefFramesInPicOrderCntCycle = 0,
            OffsetForRefFrame0 = 0,
            OffsetForRefFrame1 = 0,
            OffsetForTopToBottomField = 0,
            OffsetForNonRefPic = 0,
            PicWidthInMbsMinus1 = (ushort)((_initialWidth / 16) - 1),
            PicHeightInMapUnitsMinus1 = (ushort)((_initialHeight / 16) - 1),
            Flags = 0 // Would need to parse specific flags
        };

        _logger.LogDebug("Parsed SPS: Profile={Profile}, Level={Level}, Width={Width}MB, Height={Height}MB",
            sps.ProfileIdc, sps.LevelIdc, sps.PicWidthInMbsMinus1 + 1, sps.PicHeightInMapUnitsMinus1 + 1);

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

        // For now, create a basic PPS structure
        // In a complete implementation, this would parse the bitstream fully
        var pps = new V4L2CtrlH264Pps
        {
            PicParameterSetId = 0, // Simplified - would need bitstream parsing
            SeqParameterSetId = 0, // Links to SPS
            NumSliceGroupsMinus1 = 0, // Single slice group default
            NumRefIdxL0DefaultActiveMinus1 = 0, // Single reference default
            NumRefIdxL1DefaultActiveMinus1 = 0, // Single reference default
            WeightedBipredIdc = 0, // No weighted prediction default
            PicInitQpMinus26 = 0, // Default QP
            PicInitQsMinus26 = 0, // Default QS
            ChromaQpIndexOffset = 0, // No chroma offset
            SecondChromaQpIndexOffset = 0, // No second chroma offset
            Flags = 0 // Would need to parse specific flags
        };

        _logger.LogDebug("Parsed PPS: ID={PpsId}, SPS_ID={SpsId}, SliceGroups={SliceGroups}",
            pps.PicParameterSetId, pps.SeqParameterSetId, pps.NumSliceGroupsMinus1 + 1);

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
}