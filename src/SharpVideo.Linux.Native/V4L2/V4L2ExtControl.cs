using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// V4L2 extended control structure for complex data
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SupportedOSPlatform("linux")]
public struct V4L2ExtControl
{
    public uint Id;        // 4 bytes
    public uint Size;      // 4 bytes
    public uint Reserved2; // 4 bytes (kernel has reserved2[1], which is 1 uint)
    public IntPtr Ptr;     // 8 bytes on 64-bit
}