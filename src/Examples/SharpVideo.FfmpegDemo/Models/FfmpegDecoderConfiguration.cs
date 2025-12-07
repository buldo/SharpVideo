namespace SharpVideo.FfmpegDemo.Models;

/// <summary>
/// Configuration settings for the FFmpeg H.264 decoder
/// </summary>
public class FfmpegDecoderConfiguration
{
    /// <summary>
    /// Expected video width (informational)
    /// </summary>
    public uint Width { get; init; } = 1920;

    /// <summary>
    /// Expected video height (informational)
    /// </summary>
    public uint Height { get; init; } = 1080;

    /// <summary>
    /// Number of decoder threads (0 = auto-detect)
    /// </summary>
    public int ThreadCount { get; init; } = 0;

    /// <summary>
    /// Thread type for decoding (frame-level and slice-level parallelism)
    /// </summary>
    public int ThreadType { get; init; } = 3; // FF_THREAD_FRAME | FF_THREAD_SLICE
}
