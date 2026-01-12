using System.Linq;
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
    // V4L2 slice flags
    private const uint V4L2_H264_SLICE_FLAG_DIRECT_SPATIAL_MV_PRED = 0x01;
    private const uint V4L2_H264_SLICE_FLAG_SP_FOR_SWITCH = 0x02;

    /// <summary>
    /// Build slice params with properly constructed reference lists.
    /// Following GStreamer's gst_v4l2_codec_h264_dec_fill_slice_params and
    /// gst_v4l2_codec_h264_dec_fill_references.
    /// </summary>
    /// <param name="header">Slice header state</param>
    /// <param name="pps">PPS state</param>
    /// <param name="dpbSnapshot">V4L2 DPB entries</param>
    /// <param name="refPicList0">Constructed L0 reference list with DPB indices and fields</param>
    /// <param name="isFrame">Whether current picture is a frame (not field)</param>
    public static V4L2CtrlH264SliceParams BuildSliceParams(
        SliceHeaderState header,
        PpsState pps,
        V4L2H264DpbEntry[] dpbSnapshot,
        V4L2H264Reference[]? refPicList0,
        bool isFrame)
    {
        // Determine slice flags (matching GStreamer gst_v4l2_codec_h264_dec_fill_slice_params)
        uint flags = 0;
        if (header.direct_spatial_mv_pred_flag != 0)
        {
            flags |= V4L2_H264_SLICE_FLAG_DIRECT_SPATIAL_MV_PRED;
        }
        if (header.sp_for_switch_flag != 0)
        {
            flags |= V4L2_H264_SLICE_FLAG_SP_FOR_SWITCH;
        }

        var sliceParams = new V4L2CtrlH264SliceParams
        {
            // GStreamer: header_bit_size = 8 * nalu.header_bytes + header.header_size - 8 * n_emulation_prevention_bytes
            // We don't have this info, set to 0 (driver should handle it for frame-based mode)
            HeaderBitSize = 0,
            FirstMbInSlice = header.first_mb_in_slice,
            // slice_type uses modulo 5 per H.264 spec: 0=P, 1=B, 2=I, 3=SP, 4=SI
            SliceType = (byte)(header.slice_type % 5),
            ColourPlaneId = (byte)(header.colour_plane_id & 0x3),
            RedundantPicCnt = (byte)Math.Min(header.redundant_pic_cnt, byte.MaxValue),
            CabacInitIdc = (byte)Math.Min(header.cabac_init_idc, byte.MaxValue),
            SliceQpDelta = ClampToSByte(header.slice_qp_delta),
            SliceQsDelta = ClampToSByte(header.slice_qs_delta),
            DisableDeblockingFilterIdc = (byte)Math.Min(header.disable_deblocking_filter_idc, byte.MaxValue),
            SliceAlphaC0OffsetDiv2 = ClampToSByte(header.slice_alpha_c0_offset_div2),
            SliceBetaOffsetDiv2 = ClampToSByte(header.slice_beta_offset_div2),
            // GStreamer takes these directly from slice header
            NumRefIdxL0ActiveMinus1 = (byte)Math.Min(header.num_ref_idx_l0_active_minus1, byte.MaxValue),
            NumRefIdxL1ActiveMinus1 = (byte)Math.Min(header.num_ref_idx_l1_active_minus1, byte.MaxValue),
            Reserved = 0,
            RefPicList0 = CreateReferenceList(),
            RefPicList1 = CreateReferenceList(),
            Flags = flags
        };

        // Use provided reference list if available, otherwise fall back to simple population
        if (refPicList0 != null)
        {
            // Copy the properly constructed L0 reference list
            int l0Count = Math.Min(refPicList0.Length, sliceParams.RefPicList0.Length);
            for (int i = 0; i < l0Count; i++)
            {
                sliceParams.RefPicList0[i] = refPicList0[i];
            }
            // L1 not supported (no B-frames)
        }
        else
        {
            // Fallback to simple population (for backward compatibility)
            PopulateReferenceLists(header, dpbSnapshot, sliceParams);
        }

        return sliceParams;
    }

    private static void PopulateReferenceLists(
        SliceHeaderState header,
        V4L2H264DpbEntry[] dpbSnapshot,
        V4L2CtrlH264SliceParams sliceParams)
    {
        // Build reference lists based on active DPB entries
        // GStreamer uses lookup_dpb_index to find matching entries by reference_ts
        var activeRefs = dpbSnapshot
            .Select((entry, index) => (entry, index))
            .Where(t => (t.entry.Flags & V4L2H264Constants.V4L2_H264_DPB_ENTRY_FLAG_ACTIVE) != 0)
            .ToList();

        // Fill L0 reference list
        int l0Count = Math.Min((int)header.num_ref_idx_l0_active_minus1 + 1, activeRefs.Count);
        for (int i = 0; i < l0Count && i < sliceParams.RefPicList0.Length; i++)
        {
            sliceParams.RefPicList0[i].Fields = activeRefs[i].entry.Fields;
            sliceParams.RefPicList0[i].Index = (byte)activeRefs[i].index;
        }

        // Fill L1 reference list (for B-frames)
        int l1Count = Math.Min((int)header.num_ref_idx_l1_active_minus1 + 1, activeRefs.Count);
        for (int i = 0; i < l1Count && i < sliceParams.RefPicList1.Length; i++)
        {
            sliceParams.RefPicList1[i].Fields = activeRefs[i].entry.Fields;
            sliceParams.RefPicList1[i].Index = (byte)activeRefs[i].index;
        }
    }

    /// <summary>
    /// Creates a reference list initialized to 0xff (invalid index).
    /// V4L2 expects unused entries to have index = 0xff.
    /// </summary>
    private static V4L2H264Reference[] CreateReferenceList()
    {
        var list = new V4L2H264Reference[V4L2H264Constants.V4L2_H264_REF_LIST_LEN];
        for (int i = 0; i < list.Length; i++)
        {
            list[i].Index = 0xff;
            list[i].Fields = 0;
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
