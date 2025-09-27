namespace SharpVideo.H264;

public class H264NaluParser
{
    private readonly NaluMode _naluMode;

    public H264NaluParser(NaluMode naluMode)
    {
        _naluMode = naluMode;
    }

    public H264NaluType GetNaluType(ReadOnlySpan<byte> nalu)
    {
        if (nalu.Length == 0)
            return H264NaluType.Unspecified;

        int naluHeaderIndex = GetNaluHeaderIndex(nalu);

        if (naluHeaderIndex >= nalu.Length)
            return H264NaluType.Unspecified;

        // Extract NALU type from the header byte (bits 0-4)
        var naluHeader = nalu[naluHeaderIndex];
        var naluTypeValue = naluHeader & 0x1F;

        return (H264NaluType)naluTypeValue;
    }

    /// <summary>
    /// Gets the index of the NALU header based on the configured NALU mode
    /// </summary>
    /// <param name="nalu">The NALU data</param>
    /// <returns>Index of the NALU header byte</returns>
    private int GetNaluHeaderIndex(ReadOnlySpan<byte> nalu)
    {
        if (_naluMode == NaluMode.WithoutStartCode)
        {
            // For raw NALU data, header is at index 0
            return 0;
        }

        // For Annex-B format, need to skip start code to find header
        return GetStartCodeLength(nalu);
    }

    /// <summary>
    /// Determines the length of the start code at the beginning of the NALU
    /// </summary>
    /// <param name="nalu">The NALU data</param>
    /// <returns>Length of the start code (0, 3, or 4 bytes)</returns>
    private static int GetStartCodeLength(ReadOnlySpan<byte> nalu)
    {
        // Check for 4-byte start code: 0x00 0x00 0x00 0x01
        if (nalu.Length >= 4 &&
            nalu[0] == 0x00 && nalu[1] == 0x00 &&
            nalu[2] == 0x00 && nalu[3] == 0x01)
        {
            return 4;
        }

        // Check for 3-byte start code: 0x00 0x00 0x01
        if (nalu.Length >= 3 &&
            nalu[0] == 0x00 && nalu[1] == 0x00 && nalu[2] == 0x01)
        {
            return 3;
        }

        // No start code found
        return 0;
    }

    /// <summary>
    /// Extracts the NALU payload (header + data) based on the configured mode
    /// </summary>
    /// <param name="nalu">The NALU data</param>
    /// <returns>NALU payload without start code</returns>
    public ReadOnlySpan<byte> GetNaluPayload(ReadOnlySpan<byte> nalu)
    {
        if (nalu.Length == 0)
            return ReadOnlySpan<byte>.Empty;

        int naluHeaderIndex = GetNaluHeaderIndex(nalu);

        if (naluHeaderIndex >= nalu.Length)
            return ReadOnlySpan<byte>.Empty;

        return nalu[naluHeaderIndex..];
    }

    /// <summary>
    /// Checks if the NALU data has a valid start code (only relevant for WithStartCode mode)
    /// </summary>
    /// <param name="nalu">The NALU data</param>
    /// <returns>True if has valid start code or mode is WithoutStartCode</returns>
    public bool HasValidFormat(ReadOnlySpan<byte> nalu)
    {
        if (nalu.Length == 0)
            return false;

        if (_naluMode == NaluMode.WithoutStartCode)
        {
            // For raw NALU data, just need at least header byte
            return nalu.Length >= 1;
        }

        // For Annex-B format, must have valid start code
        int startCodeLength = GetStartCodeLength(nalu);
        return startCodeLength > 0 && nalu.Length > startCodeLength;
    }

    /// <summary>
    /// Gets the NALU header byte
    /// </summary>
    /// <param name="nalu">The NALU data</param>
    /// <returns>NALU header byte, or 0 if invalid</returns>
    public byte GetNaluHeader(ReadOnlySpan<byte> nalu)
    {
        if (nalu.Length == 0)
            return 0;

        int naluHeaderIndex = GetNaluHeaderIndex(nalu);

        if (naluHeaderIndex >= nalu.Length)
            return 0;

        return nalu[naluHeaderIndex];
    }

    /// <summary>
    /// Extracts various NALU header fields
    /// </summary>
    /// <param name="nalu">The NALU data</param>
    /// <returns>Tuple containing (forbiddenZeroBit, nalRefIdc, naluType)</returns>
    public (byte ForbiddenZeroBit, byte NalRefIdc, H264NaluType NaluType) ParseNaluHeader(ReadOnlySpan<byte> nalu)
    {
        var header = GetNaluHeader(nalu);

        var forbiddenZeroBit = (byte)((header & 0x80) >> 7);
        var nalRefIdc = (byte)((header & 0x60) >> 5);
        var naluType = (H264NaluType)(header & 0x1F);

        return (forbiddenZeroBit, nalRefIdc, naluType);
    }
}