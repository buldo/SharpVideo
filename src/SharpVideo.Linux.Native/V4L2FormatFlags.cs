namespace SharpVideo.Linux.Native;

/// <summary>
/// V4L2 format flags
/// </summary>
[Flags]
public enum V4L2FormatFlags : uint
{
    /// <summary>
    /// Compressed format
    /// </summary>
    COMPRESSED = 0x0001,

    /// <summary>
    /// Emulated format by conversion
    /// </summary>
    EMULATED = 0x0002,

    /// <summary>
    /// Continuous frame field
    /// </summary>
    CONTINUOUS_BYTESTREAM = 0x0004,

    /// <summary>
    /// Dynamic allocation
    /// </summary>
    DYN_RESOLUTION = 0x0008,

    /// <summary>
    /// Encoding parameter changes without stream restart
    /// </summary>
    ENC_CAP_FRAME_INTERVAL = 0x0010,

    /// <summary>
    /// Flag to use CSC
    /// </summary>
    CSC_COLORSPACE = 0x0020,

    /// <summary>
    /// Flag to use CSC
    /// </summary>
    CSC_XFER_FUNC = 0x0040,

    /// <summary>
    /// Flag to use CSC
    /// </summary>
    CSC_YCBCR_ENC = 0x0080,

    /// <summary>
    /// Flag to use CSC
    /// </summary>
    CSC_HSV_ENC = CSC_YCBCR_ENC,

    /// <summary>
    /// Flag to use CSC
    /// </summary>
    CSC_QUANTIZATION = 0x0100,
}