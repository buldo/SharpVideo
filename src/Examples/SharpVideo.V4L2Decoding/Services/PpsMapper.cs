using System.Runtime.Versioning;
using SharpVideo.H264;
using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2Decoding.Services;

[SupportedOSPlatform("linux")]
public static class PpsMapper
{
    public static V4L2CtrlH264Pps ConvertPpsStateToV4L2(PpsState pps)
    {
        var v4l2Pps = new V4L2CtrlH264Pps
        {
            PicParameterSetId = (byte)Math.Min(pps.pic_parameter_set_id, 255),
            SeqParameterSetId = (byte)Math.Min(pps.seq_parameter_set_id, 31),
            NumSliceGroupsMinus1 = (byte)Math.Min(pps.num_slice_groups_minus1, 7),
            NumRefIdxL0DefaultActiveMinus1 = (byte)Math.Min(pps.num_ref_idx_l0_default_active_minus1, 31),
            NumRefIdxL1DefaultActiveMinus1 = (byte)Math.Min(pps.num_ref_idx_l1_default_active_minus1, 31),
            WeightedBipredIdc = (byte)Math.Min(pps.weighted_bipred_idc, 3),
            PicInitQpMinus26 = (sbyte)Math.Max(-26, Math.Min(pps.pic_init_qp_minus26, 25)),
            PicInitQsMinus26 = (sbyte)Math.Max(-26, Math.Min(pps.pic_init_qs_minus26, 25)),
            ChromaQpIndexOffset = (sbyte)Math.Max(-12, Math.Min(pps.chroma_qp_index_offset, 12)),
            SecondChromaQpIndexOffset = (sbyte)Math.Max(-12, Math.Min(pps.second_chroma_qp_index_offset, 12)),
            Flags = 0
        };

        // Set flags based on parsed PPS values
        if (pps.entropy_coding_mode_flag != 0)
            v4l2Pps.Flags |= 0x01; // V4L2_H264_PPS_FLAG_ENTROPY_CODING_MODE

        if (pps.bottom_field_pic_order_in_frame_present_flag != 0)
            v4l2Pps.Flags |= 0x02; // V4L2_H264_PPS_FLAG_BOTTOM_FIELD_PIC_ORDER_IN_FRAME_PRESENT

        if (pps.weighted_pred_flag != 0)
            v4l2Pps.Flags |= 0x04; // V4L2_H264_PPS_FLAG_WEIGHTED_PRED

        if (pps.deblocking_filter_control_present_flag != 0)
            v4l2Pps.Flags |= 0x08; // V4L2_H264_PPS_FLAG_DEBLOCKING_FILTER_CONTROL_PRESENT

        if (pps.constrained_intra_pred_flag != 0)
            v4l2Pps.Flags |= 0x10; // V4L2_H264_PPS_FLAG_CONSTRAINED_INTRA_PRED

        if (pps.redundant_pic_cnt_present_flag != 0)
            v4l2Pps.Flags |= 0x20; // V4L2_H264_PPS_FLAG_REDUNDANT_PIC_CNT_PRESENT

        if (pps.transform_8x8_mode_flag != 0)
            v4l2Pps.Flags |= 0x40; // V4L2_H264_PPS_FLAG_TRANSFORM_8X8_MODE

        if (pps.pic_scaling_matrix_present_flag != 0)
            v4l2Pps.Flags |= 0x80; // V4L2_H264_PPS_FLAG_PIC_SCALING_MATRIX_PRESENT

        return v4l2Pps;
    }
}