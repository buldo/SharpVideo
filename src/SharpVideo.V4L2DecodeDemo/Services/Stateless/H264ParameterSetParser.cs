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