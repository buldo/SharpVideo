namespace SharpVideo.V4L2Decoding.Services;

/// <summary>
/// Exception thrown when decoder stream encounters an error (e.g., EPIPE)
/// </summary>
public class DecoderStreamException : Exception
{
    public DecoderStreamException(string message) : base(message)
    {
    }

    public DecoderStreamException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Error code from V4L2 (e.g., 32 for EPIPE)
    /// </summary>
    public int? ErrorCode { get; init; }

    /// <summary>
    /// Number of frames successfully decoded before error
    /// </summary>
    public int FramesDecoded { get; init; }
}
