using System.Runtime.Versioning;
using SharpVideo.H264;
using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2StatelessDecoder.Services;

[SupportedOSPlatform("linux")]
public static class SliceParamsMapper
{
    public static V4L2CtrlH264SliceParams BuildSliceParams(SliceHeaderState header)
    {
        var sliceParams = new V4L2CtrlH264SliceParams
        {
            HeaderBitSize = 0,
            FirstMbInSlice = header.first_mb_in_slice,
            SliceType = (byte)(header.slice_type & 0x1F),
            ColourPlaneId = (byte)(header.colour_plane_id & 0x3),
            RedundantPicCnt = (byte)Math.Min(header.redundant_pic_cnt, byte.MaxValue),
            CabacInitIdc = (byte)Math.Min(header.cabac_init_idc, byte.MaxValue),
            SliceQpDelta = ClampToSByte(header.slice_qp_delta),
            SliceQsDelta = ClampToSByte(header.slice_qs_delta),
            DisableDeblockingFilterIdc = (byte)Math.Min(header.disable_deblocking_filter_idc, byte.MaxValue),
            SliceAlphaC0OffsetDiv2 = ClampToSByte(header.slice_alpha_c0_offset_div2),
            SliceBetaOffsetDiv2 = ClampToSByte(header.slice_beta_offset_div2),
            NumRefIdxL0ActiveMinus1 = (byte)Math.Min(header.num_ref_idx_l0_active_minus1, byte.MaxValue),
            NumRefIdxL1ActiveMinus1 = (byte)Math.Min(header.num_ref_idx_l1_active_minus1, byte.MaxValue),
            Reserved = 0,
            RefPicList0 = CreateReferenceList(),
            RefPicList1 = CreateReferenceList(),
            Flags = 0
        };

        return sliceParams;
    }

    private static V4L2H264Reference[] CreateReferenceList()
    {
        return new V4L2H264Reference[V4L2H264Constants.V4L2_H264_REF_LIST_LEN];
    }

    private static sbyte ClampToSByte(int value)
    {
        if (value < sbyte.MinValue)
            return sbyte.MinValue;
        if (value > sbyte.MaxValue)
            return sbyte.MaxValue;
        return (sbyte)value;
    }
}
