namespace SharpVideo.H264;

/// <summary>
/// Table 7-1 of the 2012 standard.
/// </summary>
public enum NalUnitType : byte
{
    UNSPECIFIED_NUT = 0,
    CODED_SLICE_OF_NON_IDR_PICTURE_NUT = 1,
    CODED_SLICE_DATA_PARTITION_A_NUT = 2,
    CODED_SLICE_DATA_PARTITION_B_NUT = 3,
    CODED_SLICE_DATA_PARTITION_C_NUT = 4,
    CODED_SLICE_OF_IDR_PICTURE_NUT = 5,
    SEI_NUT = 6,
    SPS_NUT = 7,
    PPS_NUT = 8,
    AUD_NUT = 9,
    EOSEQ_NUT = 10,
    EOSTREAM_NUT = 11,
    FILLER_DATA_NUT = 12,
    SPS_EXTENSION_NUT = 13,
    PREFIX_NUT = 14,
    SUBSET_SPS_NUT = 15,
    // 16-18: reserved
    RSV16_NUT = 16,
    RSV17_NUT = 17,
    RSV18_NUT = 18,
    CODED_SLICE_OF_AUXILIARY_CODED_PICTURE_NUT = 19,
    CODED_SLICE_EXTENSION = 20,
    // 21-23: reserved
    RSV21_NUT = 21,
    RSV22_NUT = 22,
    RSV23_NUT = 23,
    // 24-29: RTP
    RTP_STAPA_NUT = 24,
    RTP_FUA_NUT = 28,
    // 24-31: unspecified
    UNSPEC24_NUT = 24,
    UNSPEC25_NUT = 25,
    UNSPEC26_NUT = 26,
    UNSPEC27_NUT = 27,
    UNSPEC28_NUT = 28,
    UNSPEC29_NUT = 29,
    UNSPEC30_NUT = 30,
    UNSPEC31_NUT = 31,
};