namespace SharpVideo.V4L2DecodeDemo.Models;

/// <summary>
/// Event arguments for decoding progress events
/// </summary>
public class DecodingProgressEventArgs : EventArgs
{
    public required long BytesProcessed { get; init; }
    public required long TotalBytes { get; init; }
    public required int FramesDecoded { get; init; }
    public required TimeSpan ElapsedTime { get; init; }

    public double ProgressPercentage => TotalBytes > 0 ? (double)BytesProcessed / TotalBytes * 100 : 0;
    public double FramesPerSecond => ElapsedTime.TotalSeconds > 0 ? FramesDecoded / ElapsedTime.TotalSeconds : 0;
}