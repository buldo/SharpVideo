using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Stateless H.264 SPS structure
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[SupportedOSPlatform("linux")]
public struct V4L2CtrlH264Sps
{
    public byte ProfileIdc;
    public byte ConstraintSetFlags;
    public byte LevelIdc;
    public byte SeqParameterSetId;
    public byte ChromaFormatIdc;
    public byte BitDepthLumaMinus8;
    public byte BitDepthChromaMinus8;
    public byte Log2MaxFrameNumMinus4;
    public byte PicOrderCntType;
    public byte Log2MaxPicOrderCntLsbMinus4;
    public byte MaxNumRefFrames;
    public byte NumRefFramesInPicOrderCntCycle;
    public short OffsetForRefFrame0;
    public short OffsetForRefFrame1;
    public short OffsetForTopToBottomField;
    public short OffsetForNonRefPic;
    public ushort PicWidthInMbsMinus1;
    public ushort PicHeightInMapUnitsMinus1;
    public uint Flags;
}