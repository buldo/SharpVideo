namespace SharpVideo.H264;

/// <summary>
/// Single SEI message payload state.
/// </summary>
public class SeiMessageState
{
    /// <summary>
    /// The type of SEI payload.
    /// </summary>
    public SeiPayloadType PayloadType { get; set; }

    /// <summary>
    /// The size of the payload in bytes.
    /// </summary>
    public uint PayloadSize { get; set; }

    /// <summary>
    /// Recovery point payload (when PayloadType == RecoveryPoint).
    /// </summary>
    public RecoveryPointState? RecoveryPoint { get; set; }
}
