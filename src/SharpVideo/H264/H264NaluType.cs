namespace SharpVideo.H264;

public enum H264NaluType : byte
{
    /// <summary>
    /// Unspecified
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Coded slice of a non-IDR picture
    /// </summary>
    CodedSliceNonIdr = 1,

    /// <summary>
    /// Coded slice data partition A
    /// </summary>
    CodedSliceDataPartitionA = 2,

    /// <summary>
    /// Coded slice data partition B
    /// </summary>
    CodedSliceDataPartitionB = 3,

    /// <summary>
    /// Coded slice data partition C
    /// </summary>
    CodedSliceDataPartitionC = 4,

    /// <summary>
    /// Coded slice of an IDR picture
    /// </summary>
    CodedSliceIdr = 5,

    /// <summary>
    /// Supplemental enhancement information (SEI)
    /// </summary>
    SupplementalEnhancementInformation = 6,

    /// <summary>
    /// Sequence parameter set (SPS)
    /// </summary>
    SequenceParameterSet = 7,

    /// <summary>
    /// Picture parameter set (PPS)
    /// </summary>
    PictureParameterSet = 8,

    /// <summary>
    /// Access unit delimiter
    /// </summary>
    AccessUnitDelimiter = 9,

    /// <summary>
    /// End of sequence
    /// </summary>
    EndOfSequence = 10,

    /// <summary>
    /// End of stream
    /// </summary>
    EndOfStream = 11,

    /// <summary>
    /// Filler data
    /// </summary>
    FillerData = 12,

    /// <summary>
    /// Sequence parameter set extension
    /// </summary>
    SequenceParameterSetExtension = 13,

    /// <summary>
    /// Prefix NAL unit
    /// </summary>
    PrefixNalUnit = 14,

    /// <summary>
    /// Subset sequence parameter set
    /// </summary>
    SubsetSequenceParameterSet = 15,

    /// <summary>
    /// Depth parameter set
    /// </summary>
    DepthParameterSet = 16,

    /// <summary>
    /// Reserved 17
    /// </summary>
    Reserved17 = 17,

    /// <summary>
    /// Reserved 18
    /// </summary>
    Reserved18 = 18,

    /// <summary>
    /// Coded slice of an auxiliary coded picture without partitioning
    /// </summary>
    CodedSliceAuxiliary = 19,

    /// <summary>
    /// Coded slice extension
    /// </summary>
    CodedSliceExtension = 20,

    /// <summary>
    /// Coded slice extension for depth view components
    /// </summary>
    CodedSliceExtensionDepthView = 21,

    /// <summary>
    /// Reserved 22
    /// </summary>
    Reserved22 = 22,

    /// <summary>
    /// Reserved 23
    /// </summary>
    Reserved23 = 23,

    /// <summary>
    /// Unspecified 24
    /// </summary>
    Unspecified24 = 24,

    /// <summary>
    /// Unspecified 25
    /// </summary>
    Unspecified25 = 25,

    /// <summary>
    /// Unspecified 26
    /// </summary>
    Unspecified26 = 26,

    /// <summary>
    /// Unspecified 27
    /// </summary>
    Unspecified27 = 27,

    /// <summary>
    /// Unspecified 28
    /// </summary>
    Unspecified28 = 28,

    /// <summary>
    /// Unspecified 29
    /// </summary>
    Unspecified29 = 29,

    /// <summary>
    /// Unspecified 30
    /// </summary>
    Unspecified30 = 30,

    /// <summary>
    /// Unspecified 31
    /// </summary>
    Unspecified31 = 31
}