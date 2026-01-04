using System.Runtime.Versioning;

using SharpVideo.H264;
using SharpVideo.Linux.Native.V4L2;

namespace SharpVideo.Decoding.V4l2.H264;

/// <summary>
/// Maps H264 slice header to V4L2 slice params control structure.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class SliceParamsMapper
{
    public static V4L2CtrlH264SliceParams BuildSliceParams(SliceHeaderState header, V4L2H264DpbEntry[] dpb)
    {
        var sliceParams = new V4L2CtrlH264SliceParams
        {
            HeaderBitSize = 0,
            FirstMbInSlice = header.first_mb_in_slice,
            SliceType = (byte)(header.slice_type % 5),
            ColourPlaneId = (byte)(header.colour_plane_id & 0x3),
            RedundantPicCnt = (byte)Math.Min(header.redundant_pic_cnt, byte.MaxValue),
            CabacInitIdc = (byte)Math.Min(header.cabac_init_idc, byte.MaxValue),
            SliceQpDelta = ClampToSByte(header.slice_qp_delta),
            SliceQsDelta = ClampToSByte(header.slice_qs_delta),
            DisableDeblockingFilterIdc = (byte)Math.Min(header.disable_deblocking_filter_idc, byte.MaxValue),
            SliceAlphaC0OffsetDiv2 = ClampToSByte(header.slice_alpha_c0_offset_div2),
            SliceBetaOffsetDiv2 = ClampToSByte(header.slice_beta_offset_div2),
            NumRefIdxL0ActiveMinus1 = (byte)Math.Min(header.num_ref_idx_l0_active_minus1, byte.MaxValue),
            NumRefIdxL1ActiveMinus1 = 0,
            Reserved = 0,
            RefPicList0 = BuildRefPicList(dpb),
            RefPicList1 = CreateEmptyRefPicList(),
            Flags = (header.sp_for_switch_flag != 0 ? 0x02u : 0)
        };

        return sliceParams;
    }

    private static V4L2H264Reference[] CreateEmptyRefPicList()
    {
        var list = new V4L2H264Reference[V4L2H264Constants.V4L2_H264_REF_LIST_LEN];
        for (int i = 0; i < list.Length; i++)
        {
            list[i].Index = 0xFF;
        }
        return list;
    }

    private static V4L2H264Reference[] BuildRefPicList(V4L2H264DpbEntry[] dpb)
    {
        var list = new V4L2H264Reference[V4L2H264Constants.V4L2_H264_REF_LIST_LEN];
        
        // Find all ACTIVE reference frames in DPB
        var activeRefs = new List<(int index, V4L2H264DpbEntry entry)>();
        for (int i = 0; i < dpb.Length; i++)
        {
            if ((dpb[i].Flags & V4L2H264Constants.V4L2_H264_DPB_ENTRY_FLAG_ACTIVE) != 0)
            {
                activeRefs.Add((i, dpb[i]));
            }
        }

        // Default H.264 sorting for RefPicList0 (P-frame): 
        // Short-term: PicNum descending. Long-term: LongTermPicNum ascending.
        // For simplicity and our FIFO DPB, we just sort by PicNum descending.
        activeRefs.Sort((a, b) => b.entry.PicNum.CompareTo(a.entry.PicNum));

        for (int i = 0; i < activeRefs.Count && i < list.Length; i++)
        {
            list[i].Index = (byte)activeRefs[i].index;
            list[i].Fields = 0; // 0 = frame, 1 = top field, 2 = bottom field
        }

        // Initialize remaining entries with invalid index
        for (int i = activeRefs.Count; i < list.Length; i++)
        {
            list[i].Index = 0xFF;
        }

        return list;
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
