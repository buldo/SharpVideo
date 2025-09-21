using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

public class V4L2DeviceControl
{
    public required uint Id { get; init; }
    public required V4L2CtrlType Type { get; init; }
    public required string Name { get; init; }
    public required long Minimum { get; init; }
    public required long Maximum { get; init; }
    public required long Step { get; init; }
    public required long DefaultValue { get; init; }
    public required V4L2ControlFlags Flags { get; init; }

    // Extended fields (present for compound/array controls)
    public uint? ElemSize { get; init; }
    public uint? Elems { get; init; }
    public uint? NrOfDims { get; init; }
    public uint[]? Dims { get; init; }

    // For Menu / IntegerMenu convenience
    public IReadOnlyList<(uint Index, string? Name, long? Value)>? MenuItems { get; init; }
}