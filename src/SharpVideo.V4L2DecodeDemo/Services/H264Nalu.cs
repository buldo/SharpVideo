namespace SharpVideo.V4L2DecodeDemo.Services;

/// <summary>
/// Represents a single H.264 NALU (Network Abstraction Layer Unit)
/// </summary>
public class H264Nalu
{
    /// <summary>
    /// The NALU type (5 bits from the NALU header)
    /// </summary>
    public byte Type { get; init; }

    /// <summary>
    /// The raw NALU data including the start code
    /// </summary>
    public required byte[] Data { get; init; }

    /// <summary>
    /// The position of this NALU in the original stream
    /// </summary>
    public long Position { get; init; }

    /// <summary>
    /// Whether this NALU is a keyframe (contains SPS, PPS, or IDR slice)
    /// </summary>
    public bool IsKeyFrame { get; init; }

    /// <summary>
    /// Human-readable description of the NALU type
    /// </summary>
    public string TypeDescription => GetNaluTypeDescription(Type);

    /// <summary>
    /// Gets a description for a NALU type
    /// </summary>
    private static string GetNaluTypeDescription(byte naluType)
    {
        return naluType switch
        {
            1 => "Non-IDR Slice",
            2 => "Data Partition A",
            3 => "Data Partition B",
            4 => "Data Partition C",
            5 => "IDR Slice",
            6 => "SEI (Supplemental Enhancement Information)",
            7 => "SPS (Sequence Parameter Set)",
            8 => "PPS (Picture Parameter Set)",
            9 => "Access Unit Delimiter",
            10 => "End of Sequence",
            11 => "End of Stream",
            12 => "Filler Data",
            13 => "SPS Extension",
            14 => "Prefix NAL Unit",
            15 => "Subset SPS",
            19 => "Auxiliary Coded Picture",
            20 => "Coded Slice Extension",
            _ => $"Unknown ({naluType})"
        };
    }
}