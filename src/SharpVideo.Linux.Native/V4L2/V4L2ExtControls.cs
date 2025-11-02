using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// V4L2 extended controls container
/// </summary>
[StructLayout(LayoutKind.Explicit)]
[SupportedOSPlatform("linux")]
public struct V4L2ExtControls
{
    [FieldOffset(0)]
    public uint CtrlClass;

    [FieldOffset(0)]
    public uint Which;

    [FieldOffset(4)]
    public uint Count;

    [FieldOffset(8)]
    public uint ErrorIdx;

    [FieldOffset(12)]
    public int RequestFd;

    [FieldOffset(16)]
    public uint Reserved;

    [FieldOffset(24)]
    public IntPtr Controls;
}