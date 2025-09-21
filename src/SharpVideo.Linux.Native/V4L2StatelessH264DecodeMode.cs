namespace SharpVideo.Linux.Native;

/// <summary>
/// Decoding mode
/// </summary>
public enum V4L2StatelessH264DecodeMode
{
    /// <summary>
    /// Indicates that decoding is performed one slice at a time.
    /// In this mode, V4L2_CID_STATELESS_H264_SLICE_PARAMS must contain the parsed slice
    /// parameters and the OUTPUT buffer must contain a single slice.
    /// V4L2_BUF_CAP_SUPPORTS_M2M_HOLD_CAPTURE_BUF feature is used in order to support multislice frames.
    /// </summary>
    SLICE_BASED,

    /// <summary>
    /// Indicates that decoding is performed per frame.
    /// The OUTPUT buffer must contain all slices and also both fields.
    /// This mode is typically supported by device drivers that are able to parse the slice(s) header(s) in hardware.
    /// When this mode is selected,When this mode is selected, V4L2_CID_STATELESS_H264_SLICE_PARAMS is not used.
    /// </summary>
    FRAME_BASED,
};