namespace SharpVideo.H264;

/// <summary>
/// Section A.2 Profiles
/// </summary>
public enum ProfileType : byte
{
    UNSPECIFIED = 0,
    BASELINE = 1,
    CONSTRAINED_BASELINE = 2,
    MAIN = 3,
    EXTENDED = 4,
    HIGH = 5,
    PROGRESSIVE_HIGH = 6,
    CONSTRAINED_HIGH = 7,
    HIGH_10 = 8,
    PROGRESSIVE_HIGH_10 = 9,
    HIGH_10_INTRA = 10,
    HIGH_422 = 11,
    HIGH_422_INTRA = 12,
    HIGH_444 = 13,
    HIGH_444_INTRA = 14,
    HIGH_444_PRED = 15,
    HIGH_444_PRED_INTRA = 16,
    CAVLC_444_INTRA = 17,
};