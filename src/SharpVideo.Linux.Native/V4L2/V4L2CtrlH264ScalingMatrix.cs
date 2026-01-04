using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// H264 scaling matrices parameters.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SupportedOSPlatform("linux")]
public struct V4L2CtrlH264ScalingMatrix
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6 * 16)]
    public byte[] scaling_list_4x4;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6 * 64)]
    public byte[] scaling_list_8x8;
}
