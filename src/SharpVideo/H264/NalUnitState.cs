namespace SharpVideo.H264;

/// <summary>
/// The parsed state of the NAL Unit. Only some select values are stored.
/// Add more as they are actually needed.
/// </summary>
public class NalUnitState
{
    /// <summary>
    /// NAL Unit offset in the full blob
    /// </summary>
    public int offset;

    /// <summary>
    /// NAL Unit length
    /// </summary>
    public int length;

    /// <summary>
    /// NAL Unit parsed length
    /// </summary>
    public int parsed_length;

    /// <summary>
    /// NAL Unit checksum
    /// </summary>
    public NaluChecksum checksum;

    public NalUnitHeaderState nal_unit_header;
    public NalUnitPayloadState nal_unit_payload;
}