namespace SharpVideo.H264;

/// <summary>
/// Specifies how NALU data should be formatted in the output
/// </summary>
public enum NaluOutputMode
{
    /// <summary>
    /// Include the start code with each NALU (Annex-B format)
    /// </summary>
    WithStartCode,

    /// <summary>
    /// Exclude the start code from each NALU (raw NALU data only)
    /// </summary>
    WithoutStartCode
}