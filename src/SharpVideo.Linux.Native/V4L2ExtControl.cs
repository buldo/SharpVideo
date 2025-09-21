using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

/// <summary>
/// V4L2 extended control structure for complex data
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[SupportedOSPlatform("linux")]
public struct V4L2ExtControl
{
    public uint Id;
    public uint Size;
    public uint Reserved2;
    public IntPtr Ptr;
}