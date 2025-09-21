using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Stateless H.264 PPS structure
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[SupportedOSPlatform("linux")]
public struct V4L2CtrlH264Pps
{
    public byte PicParameterSetId;
    public byte SeqParameterSetId;
    public byte NumSliceGroupsMinus1;
    public byte NumRefIdxL0DefaultActiveMinus1;
    public byte NumRefIdxL1DefaultActiveMinus1;
    public byte WeightedBipredIdc;
    public sbyte PicInitQpMinus26;
    public sbyte PicInitQsMinus26;
    public sbyte ChromaQpIndexOffset;
    public sbyte SecondChromaQpIndexOffset;
    public ushort Flags;
}