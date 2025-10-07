namespace SharpVideo.V4L2DecodeDemo.Models;

/// <summary>
/// Event arguments for frame decoded events
/// </summary>
public class FrameDecodedEventArgs : EventArgs
{
    public required int FrameNumber { get; init; }
    public required uint BytesUsed { get; init; }
    public required DateTime Timestamp { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required uint PixelFormat { get; init; }
    public required int PlaneCount { get; init; }

    /// <summary>
    /// Optional callback to extract frame data. Only call this if you need the actual frame pixels.
    /// This is an expensive operation (copies ~3MB for 1080p)
    /// </summary>
    public Func<byte[]>? ExtractFrameData { get; init; }
}