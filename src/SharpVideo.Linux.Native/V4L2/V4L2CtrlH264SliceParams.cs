using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native.V4L2;

public static class V4L2H264Constants
{
    public const int V4L2_H264_NUM_DPB_ENTRIES = 16;
    public const int V4L2_H264_REF_LIST_LEN = V4L2_H264_NUM_DPB_ENTRIES * 2;

    public const uint V4L2_H264_DPB_ENTRY_FLAG_VALID = 0x01;
    public const uint V4L2_H264_DPB_ENTRY_FLAG_ACTIVE = 0x02;
    public const uint V4L2_H264_DPB_ENTRY_FLAG_LONG_TERM = 0x04;
    public const uint V4L2_H264_DPB_ENTRY_FLAG_FIELD = 0x08;

    public const uint V4L2_H264_DECODE_PARAM_FLAG_IDR_PIC = 0x01;
    public const uint V4L2_H264_DECODE_PARAM_FLAG_PFRAME = 0x08;
    public const uint V4L2_H264_DECODE_PARAM_FLAG_BFRAME = 0x10;
}

/// <summary>
/// Reference entry used in slice parameter reference lists.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SupportedOSPlatform("linux")]
public struct V4L2H264Reference
{
    public byte Fields;
    public byte Index;
}

/// <summary>
/// Stateless H.264 slice parameters structure
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SupportedOSPlatform("linux")]
public struct V4L2CtrlH264SliceParams
{
    public uint HeaderBitSize;
    public uint FirstMbInSlice;
    public byte SliceType;
    public byte ColourPlaneId;
    public byte RedundantPicCnt;
    public byte CabacInitIdc;
    public sbyte SliceQpDelta;
    public sbyte SliceQsDelta;
    public byte DisableDeblockingFilterIdc;
    public sbyte SliceAlphaC0OffsetDiv2;
    public sbyte SliceBetaOffsetDiv2;
    public byte NumRefIdxL0ActiveMinus1;
    public byte NumRefIdxL1ActiveMinus1;
    public byte Reserved;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = V4L2H264Constants.V4L2_H264_REF_LIST_LEN)]
    public V4L2H264Reference[] RefPicList0;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = V4L2H264Constants.V4L2_H264_REF_LIST_LEN)]
    public V4L2H264Reference[] RefPicList1;

    public uint Flags;
}