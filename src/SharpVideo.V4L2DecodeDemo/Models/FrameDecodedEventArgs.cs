namespace SharpVideo.V4L2DecodeDemo.Models;

/// <summary>
/// Event arguments for frame decoded events
/// </summary>
public class FrameDecodedEventArgs : EventArgs
{
    public required int FrameNumber { get; init; }
    public required uint BufferIndex { get; init; }
    public required uint BytesUsed { get; init; }
    public required DateTime Timestamp { get; init; }
    public uint Width { get; init; }
    public uint Height { get; init; }
    public uint PixelFormat { get; init; }
}