namespace SharpVideo.V4L2Decoding.Services;

/// <summary>
/// Types of H.264 V4L2 decoders
/// </summary>
public enum DecoderType
{
    /// <summary>
    /// Stateless decoder - requires application to manage decode state
  /// </summary>
    Stateless,

    /// <summary>
    /// Stateful decoder - hardware manages decode state
    /// </summary>
    Stateful
}
