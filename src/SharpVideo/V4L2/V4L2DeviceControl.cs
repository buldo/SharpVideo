using SharpVideo.Linux.Native.V4L2;

namespace SharpVideo.V4L2;

public class V4L2DeviceControl
{
    public required uint Id { get; init; }
    public required V4L2CtrlType Type { get; init; }
    public required string Name { get; init; }
    public required int Minimum { get; init; }
    public required int Maximum { get; init; }
    public required int Step { get; init; }
    public required int DefaultValue { get; init; }
    public required V4L2ControlFlags Flags { get; init; }
}