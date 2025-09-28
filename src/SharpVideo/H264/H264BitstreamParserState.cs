namespace SharpVideo.H264;

/// <summary>
/// A class for keeping the state of a H264 Bitstream.
/// The parsed state of the bitstream.
/// </summary>
public class H264BitstreamParserState
{
    /// <summary>
    /// SPS state
    /// </summary>
    public Dictionary<uint32_t, SpsState> sps = new();

    /// <summary>
    /// PPS state
    /// </summary>
    public Dictionary<uint32_t, PpsState> pps = new();

    /// <summary>
    /// SubsetSPS state
    /// </summary>
    public Dictionary<uint32_t, SubsetSpsState> subset_sps = new();

// some accessors
    SpsState? GetSps(uint32_t sps_id) => sps.GetValueOrDefault(sps_id);
    PpsState? GetPps(uint32_t pps_id) => pps.GetValueOrDefault(pps_id);
    //SubsetSpsState GetSubsetSps(uint32_t subset_sps_id);
}