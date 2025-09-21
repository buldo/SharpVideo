using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Stateless H.264 SPS structure - matches kernel v4l2_ctrl_h264_sps
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
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

    /// <summary>
    /// offset_for_ref_frame[255] - large array for reference frame offsets
    /// This is the critical field that was missing and causing EINVAL errors
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 255)]
    public int[] OffsetForRefFrame;

    public int OffsetForNonRefPic;
    public int OffsetForTopToBottomField;
    public ushort PicWidthInMbsMinus1;
    public ushort PicHeightInMapUnitsMinus1;
    public uint Flags;
}