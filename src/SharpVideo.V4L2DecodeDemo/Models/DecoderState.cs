namespace SharpVideo.V4L2DecodeDemo.Models;

/// <summary>
/// Decoder state enumeration
/// </summary>
public enum DecoderState
{
    /// <summary>
    /// Decoder is not initialized
    /// </summary>
    Uninitialized,

    /// <summary>
    /// Decoder is initializing
    /// </summary>
    Initializing,

    /// <summary>
    /// Decoder is ready for operation
    /// </summary>
    Ready,

    /// <summary>
    /// Decoder is actively decoding
    /// </summary>
    Decoding,

    /// <summary>
    /// Decoder encountered an error
    /// </summary>
    Error,

    /// <summary>
    /// Decoder is cleaning up resources
    /// </summary>
    Disposing,

    /// <summary>
    /// Decoder has been disposed
    /// </summary>
    Disposed
}

/// <summary>
/// Decoder state information
/// </summary>
public class DecoderStateInfo
{
    public DecoderState State { get; set; } = DecoderState.Uninitialized;
    public string? LastError { get; set; }
    public DateTime LastStateChange { get; set; } = DateTime.Now;
    public int FramesDecoded { get; set; }
    public long BytesProcessed { get; set; }
    public TimeSpan TotalDecodingTime { get; set; }
}