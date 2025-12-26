namespace SharpVideo.Utils.Buffers;

public class BufferParams
{
    public required uint Width { get; init; }
    public required uint Height { get; init; }
    public required ulong FullSize { get; init; }

    public required int PlanesCount { get; init; }

    public required IReadOnlyList<ulong> PlaneOffsets { get; init; }

    public required uint Stride { get; init; }
}