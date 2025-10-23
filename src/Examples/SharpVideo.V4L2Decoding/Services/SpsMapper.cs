using System.Runtime.Versioning;
using SharpVideo.H264;
using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2Decoding.Services;

[SupportedOSPlatform("linux")]
public static class SpsMapper
{
    public static V4L2CtrlH264Sps MapSpsToV4L2(SpsState original)
    {
        var spsData = original.sps_data;
        var constraintSetFlags = GetConstraintSetFlags(spsData);
        var spsFlags = GetSpsFlags(spsData);

        var ret = new V4L2CtrlH264Sps()
        {
            bit_depth_chroma_minus8 = (byte)spsData.bit_depth_chroma_minus8,
            bit_depth_luma_minus8 = (byte)spsData.bit_depth_luma_minus8,
            chroma_format_idc = (byte)spsData.chroma_format_idc,
            constraint_set_flags = constraintSetFlags,
            flags = spsFlags,
            level_idc = (byte)spsData.level_idc,
            log2_max_frame_num_minus4 = (byte)spsData.log2_max_frame_num_minus4,
            log2_max_pic_order_cnt_lsb_minus4 = (byte)spsData.log2_max_pic_order_cnt_lsb_minus4,
            max_num_ref_frames = (byte)spsData.max_num_ref_frames,
            num_ref_frames_in_pic_order_cnt_cycle = (byte)spsData.num_ref_frames_in_pic_order_cnt_cycle,
            offset_for_non_ref_pic = spsData.offset_for_non_ref_pic,
            offset_for_top_to_bottom_field = spsData.offset_for_top_to_bottom_field,
            pic_height_in_map_units_minus1 = (ushort)spsData.pic_height_in_map_units_minus1,
            pic_order_cnt_type = (byte)spsData.pic_order_cnt_type,
            pic_width_in_mbs_minus1 = (ushort)spsData.pic_width_in_mbs_minus1,
            profile_idc = (byte)spsData.profile_idc,
            seq_parameter_set_id = (byte)spsData.seq_parameter_set_id,
        };

        ret.offset_for_ref_frame = new int[255];
        if(spsData.offset_for_ref_frame != null)
        {
            for(int i = 0; i < spsData.offset_for_ref_frame.Count && i < 255; i++)
            {
                ret.offset_for_ref_frame[i] = spsData.offset_for_ref_frame[i];
            }
        }


        return ret;
    }

    private static V4L2H264SpsFlag GetSpsFlags(SpsDataState spsData)
    {
        V4L2H264SpsFlag flags = 0;
        if (spsData.separate_colour_plane_flag != 0)
        {
            flags |= V4L2H264SpsFlag.V4L2_H264_SPS_FLAG_SEPARATE_COLOUR_PLANE;
        }

        if (spsData.qpprime_y_zero_transform_bypass_flag != 0)
        {
            flags |= V4L2H264SpsFlag.V4L2_H264_SPS_FLAG_QPPRIME_Y_ZERO_TRANSFORM_BYPASS;
        }

        if (spsData.delta_pic_order_always_zero_flag != 0)
        {
            flags |= V4L2H264SpsFlag.V4L2_H264_SPS_FLAG_DELTA_PIC_ORDER_ALWAYS_ZERO;
        }

        if (spsData.gaps_in_frame_num_value_allowed_flag != 0)
        {
            flags |= V4L2H264SpsFlag.V4L2_H264_SPS_FLAG_GAPS_IN_FRAME_NUM_VALUE_ALLOWED;
        }

        if (spsData.frame_mbs_only_flag != 0)
        {
            flags |= V4L2H264SpsFlag.V4L2_H264_SPS_FLAG_FRAME_MBS_ONLY;
        }

        if (spsData.mb_adaptive_frame_field_flag != 0)
        {
            flags |= V4L2H264SpsFlag.V4L2_H264_SPS_FLAG_MB_ADAPTIVE_FRAME_FIELD;
        }

        if (spsData.direct_8x8_inference_flag!= 0)
        {
            flags |= V4L2H264SpsFlag.V4L2_H264_SPS_FLAG_DIRECT_8X8_INFERENCE;
        }

        return flags;
    }

    private static V4L2H264SpsConstraintSetFlag GetConstraintSetFlags(SpsDataState spsData)
    {
        V4L2H264SpsConstraintSetFlag constraintSetFlags = 0;
        if (spsData.constraint_set0_flag != 0)
        {
            constraintSetFlags |= V4L2H264SpsConstraintSetFlag.V4L2_H264_SPS_CONSTRAINT_SET0_FLAG;
        }
        if (spsData.constraint_set1_flag != 0)
        {
            constraintSetFlags |= V4L2H264SpsConstraintSetFlag.V4L2_H264_SPS_CONSTRAINT_SET1_FLAG;
        }
        if (spsData.constraint_set2_flag != 0)
        {
            constraintSetFlags |= V4L2H264SpsConstraintSetFlag.V4L2_H264_SPS_CONSTRAINT_SET2_FLAG;
        }
        if (spsData.constraint_set3_flag != 0)
        {
            constraintSetFlags |= V4L2H264SpsConstraintSetFlag.V4L2_H264_SPS_CONSTRAINT_SET3_FLAG;
        }
        if (spsData.constraint_set4_flag != 0)
        {
            constraintSetFlags |= V4L2H264SpsConstraintSetFlag.V4L2_H264_SPS_CONSTRAINT_SET4_FLAG;
        }
        if (spsData.constraint_set5_flag != 0)
        {
            constraintSetFlags |= V4L2H264SpsConstraintSetFlag.V4L2_H264_SPS_CONSTRAINT_SET5_FLAG;
        }

        return constraintSetFlags;
    }
}