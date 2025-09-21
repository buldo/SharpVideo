using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

/// <summary>
/// V4L2 extended controls container
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[SupportedOSPlatform("linux")]
public struct V4L2ExtControls
{
    public uint Which;
    public uint Count;
    public uint ErrorIdx;
    public uint Reserved1;
    public uint Reserved2;
    public IntPtr Controls;
}