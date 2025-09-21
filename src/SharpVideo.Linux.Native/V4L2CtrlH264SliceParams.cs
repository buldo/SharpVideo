using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Stateless H.264 slice parameters structure
/// </summary>
[StructLayout(LayoutKind.Sequential)]
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
    public uint Flags;
    // Note: Reference lists would go here in a complete implementation
}