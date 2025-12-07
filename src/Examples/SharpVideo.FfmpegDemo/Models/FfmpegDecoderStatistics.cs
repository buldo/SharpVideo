namespace SharpVideo.FfmpegDemo.Models;

/// <summary>
/// Statistics collected during FFmpeg decoding
/// </summary>
public class FfmpegDecoderStatistics
{
    /// <summary>
    /// Total time spent decoding
    /// </summary>
    public TimeSpan DecodeElapsed { get; set; }

    /// <summary>
    /// Total frames decoded
    /// </summary>
    public int FramesDecoded { get; set; }

    /// <summary>
    /// Total packets sent to decoder
    /// </summary>
    public int PacketsSent { get; set; }

    /// <summary>
    /// Average frames per second
    /// </summary>
    public double AverageFps => DecodeElapsed.TotalSeconds > 0 
        ? FramesDecoded / DecodeElapsed.TotalSeconds 
        : 0;
}
