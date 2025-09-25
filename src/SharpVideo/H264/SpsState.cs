namespace SharpVideo.H264;

/// <summary>
/// The parsed state of the SPS. Only some select values are stored.
/// Add more as they are actually needed.
/// </summary>
public class SpsState
{

#if DEBUG
//void fdump(FILE* outfp, int indent_level, ParsingOptions parsing_options) const;
#endif // FDUMP_DEFINE

    public SpsDataState sps_data;

    /// <summary>
    /// derived values
    /// </summary>
    public UInt32 getChromaArrayType()
    {
        return sps_data.getChromaArrayType();
    }
}