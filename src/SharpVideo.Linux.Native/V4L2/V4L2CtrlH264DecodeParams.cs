using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// Decoded picture buffer entry for stateless H.264 decoding.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SupportedOSPlatform("linux")]
public struct V4L2H264DpbEntry
{
    public ulong ReferenceTimestamp;
    public uint PicNum;
    public ushort FrameNum;
    public byte Fields;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
    public byte[] Reserved;

    public int TopFieldOrderCnt;
    public int BottomFieldOrderCnt;
    public uint Flags;
}

/// <summary>
/// Stateless H.264 decode parameters structure
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SupportedOSPlatform("linux")]
public struct V4L2CtrlH264DecodeParams
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = V4L2H264Constants.V4L2_H264_NUM_DPB_ENTRIES)]
    public V4L2H264DpbEntry[] Dpb;

    public ushort NalRefIdc;
    public ushort FrameNum;
    public int TopFieldOrderCnt;
    public int BottomFieldOrderCnt;
    public ushort IdrPicId;
    public ushort PicOrderCntLsb;
    public int DeltaPicOrderCntBottom;
    public int DeltaPicOrderCnt0;
    public int DeltaPicOrderCnt1;
    public uint DecRefPicMarkingBitSize;
    public uint PicOrderCntBitSize;
    public uint SliceGroupChangeCycle;
    public uint Reserved;
    public uint Flags;
}