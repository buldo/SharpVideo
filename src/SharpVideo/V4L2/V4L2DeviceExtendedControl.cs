using SharpVideo.Linux.Native.V4L2;

namespace SharpVideo.V4L2;

public class V4L2DeviceExtendedControl
{
    public required uint Id { get; init; }
    public required V4L2CtrlType Type { get; init; }
    public required string Name { get; init; }
    public required long Minimum { get; init; }
    public required long Maximum { get; init; }
    public required long Step { get; init; }
    public required long DefaultValue { get; init; }
    public required V4L2ControlFlags Flags { get; init; }
    public required uint ElemSize { get; init; }
    public required uint Elems { get; init; }
    public required uint NrOfDims { get; init; }
    public required uint[] Dims { get; init; }
}