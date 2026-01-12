namespace SharpVideo.H264;

/// <summary>
/// State container for all parsed SEI messages in a NAL unit.
/// </summary>
public class SeiRbspState
{
    /// <summary>
    /// List of all parsed SEI messages.
    /// </summary>
    public List<SeiMessageState> Messages { get; } = new();

    /// <summary>
    /// Gets the recovery point SEI if present.
    /// </summary>
    public RecoveryPointState? RecoveryPoint =>
        Messages.FirstOrDefault(m => m.PayloadType == SeiPayloadType.RecoveryPoint)?.RecoveryPoint;
}
