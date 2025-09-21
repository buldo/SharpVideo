using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Stateless H.264 decode parameters structure
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[SupportedOSPlatform("linux")]
public struct V4L2CtrlH264DecodeParams
{
    public ushort FrameNum;
    public ushort IdrPicId;
    public ushort PicOrderCntLsb;
    public int DeltaPicOrderCntBottom;
    public int DeltaPicOrderCnt0;
    public int DeltaPicOrderCnt1;
    public uint DecRefPicMarkingBitSize;
    public uint PicOrderCntBitSize;
    public uint SliceGroupChangeCycle;
    public uint Flags;
    // Note: DPB entries would go here in a complete implementation
}