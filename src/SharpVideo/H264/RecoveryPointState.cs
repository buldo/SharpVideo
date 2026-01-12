namespace SharpVideo.H264;

/// <summary>
/// Recovery point SEI message state.
/// Defined in section D.1.7 of the H.264/AVC specification.
/// This SEI message is used for intra refresh (gradual decoder refresh).
/// </summary>
public class RecoveryPointState
{
    /// <summary>
    /// Specifies the recovery point of output pictures in output order.
    /// A recovery point frame is defined as a frame after which all pictures
    /// can be correctly decoded with respect to referenced pictures.
    /// </summary>
    public uint RecoveryFrameCnt { get; set; }

    /// <summary>
    /// Indicates whether the decoding of the recovery point picture will
    /// result in an output picture that is an exact match with the output
    /// that would be generated for the picture if it were an IDR.
    /// </summary>
    public bool ExactMatchFlag { get; set; }

    /// <summary>
    /// Indicates whether the pictures produced by the decoder until recovery
    /// are considered to have broken links and may contain artifacts.
    /// </summary>
    public bool BrokenLinkFlag { get; set; }

    /// <summary>
    /// Indicates the slice group change direction. Range 0-2.
    /// </summary>
    public uint ChangingSliceGroupIdc { get; set; }
}
