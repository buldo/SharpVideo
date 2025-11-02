using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// V4L2 control structure
/// </summary>
/// <summary>
/// V4L2 control structure for setting controls
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[SupportedOSPlatform("linux")]
public struct V4L2Control
{
    public uint Id;
    public int Value;
}