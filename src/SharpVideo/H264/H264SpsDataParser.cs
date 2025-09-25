namespace SharpVideo.H264;

public class H264SpsDataParser
{
    // Section 7.4.2.1.1: "The value of chroma_format_idc shall be in the
    // range of 0 to 3, inclusive."
    const UInt32 kChromaFormatIdcMin = 0;
    const UInt32 kChromaFormatIdcMax = 3;

    // Section 7.4.2.1.1: "bit_depth_luma_minus8 shall be in the range of 0 to
    // 6, inclusive."
    const UInt32 kBitDepthLumaMinus8Min = 0;
    const UInt32 kBitDepthLumaMinus8Max = 6;

    // Section 7.4.2.1.1: "bit_depth_chroma_minus8 shall be in the range of 0 to
    // 6, inclusive."
    const UInt32 kBitDepthChromaMinus8Min = 0;
    const UInt32 kBitDepthChromaMinus8Max = 6;

    // Section 7.4.2.1.2: "The value of seq_parameter_set_id shall be in the
    // range of 0 to 31, inclusive."
    const UInt32 kSeqParameterSetIdMin = 0;
    const UInt32 kSeqParameterSetIdMax = 31;

    // Section 7.4.2.1.1: "The value of log2_max_frame_num_minus4 shall be in
    // the range of 0 to 12, inclusive."
    const UInt32 kLog2MaxFrameNumMinus4Min = 0;
    const UInt32 kLog2MaxFrameNumMinus4Max = 12;

    // Section 7.4.2.1.1: "The value of pic_order_cnt_type shall be in the
    // range of 0 to 2, inclusive."
    const UInt32 kPicOrderCntTypeMin = 0;
    const UInt32 kPicOrderCntTypeMax = 2;

    // Section 7.4.2.1.1: "The value of log2_max_pic_order_cnt_lsb_minus4
    // shall be in the range of 0 to 12, inclusive."
    const UInt32 kLog2MaxPicOrderCntLsbMinus4Min = 0;
    const UInt32 kLog2MaxPicOrderCntLsbMinus4Max = 12;

    // Section 7.4.2.1.1: "The value of num_ref_frames_in_pic_order_cnt_cycle
    // shall be in the range of 0 to 255, inclusive."
    const UInt32 kNumRefFramesInPicOrderCntCycleMin = 12;
    const UInt32 kNumRefFramesInPicOrderCntCycleMax = 255;


    // Unpack RBSP and parse SPS data state from the supplied buffer.
    static SpsDataState? ParseSpsData(ReadOnlySpan<byte> data)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new BitBuffer(unpacked_buffer.ToArray());
        return ParseSpsData(bit_buffer);
    }

    public static SpsDataState? ParseSpsData(BitBuffer bit_buffer)
    {
        UInt32 bits_tmp;
        Int32 golomb_tmp;

        // H264 SPS Nal Unit (seq_parameter_set_data(()) parser.
        // Section 7.3.2.1.1 ("Sequence parameter set data syntax") of the H.264
        // standard for a complete description.
        var sps_data = new SpsDataState();

        // profile_idc  u(8)
        if (!bit_buffer.ReadBits(8, out sps_data.profile_idc))
        {
            return null;
        }

        // constraint_set0_flag  u(1)
        if (!bit_buffer.ReadBits(1, out sps_data.constraint_set0_flag))
        {
            return null;
        }

        // constraint_set1_flag  u(1)
        if (!bit_buffer.ReadBits(1, out sps_data.constraint_set1_flag))
        {
            return null;
        }

        // constraint_set2_flag  u(1)
        if (!bit_buffer.ReadBits(1, out sps_data.constraint_set2_flag))
        {
            return null;
        }

        // constraint_set3_flag  u(1)
        if (!bit_buffer.ReadBits(1, out sps_data.constraint_set3_flag))
        {
            return null;
        }

        // constraint_set4_flag  u(1)
        if (!bit_buffer.ReadBits(1, out sps_data.constraint_set4_flag))
        {
            return null;
        }

        // constraint_set5_flag  u(1)
        if (!bit_buffer.ReadBits(1, out sps_data.constraint_set5_flag))
        {
            return null;
        }

        // reserved_zero_2bits  u(2)
        if (!bit_buffer.ReadBits(2, out sps_data.reserved_zero_2bits))
        {
            return null;
        }

        // derive the profile
        sps_data.profile_type = sps_data.GetProfileType();

        // level_idc  u(8)
        if (!bit_buffer.ReadBits(8, out sps_data.level_idc))
        {
            return null;
        }

        // seq_parameter_set_id  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out sps_data.seq_parameter_set_id))
        {
            return null;
        }
        if (sps_data.seq_parameter_set_id < kSeqParameterSetIdMin ||
            sps_data.seq_parameter_set_id > kSeqParameterSetIdMax)
        {
#if DEBUG
            //fprintf(stderr, "invalid seq_parameter_set_id: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", sps_data.seq_parameter_set_id, kSeqParameterSetIdMin, kSeqParameterSetIdMax);
#endif  // FPRINT_ERRORS
            return null;
        }

        if (sps_data.profile_idc == 100 || sps_data.profile_idc == 110 ||
            sps_data.profile_idc == 122 || sps_data.profile_idc == 244 ||
            sps_data.profile_idc == 44 || sps_data.profile_idc == 83 ||
            sps_data.profile_idc == 86 || sps_data.profile_idc == 118 ||
            sps_data.profile_idc == 128 || sps_data.profile_idc == 138 ||
            sps_data.profile_idc == 139 || sps_data.profile_idc == 134 ||
            sps_data.profile_idc == 135)
        {
            // chroma_format_idc  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out sps_data.chroma_format_idc))
            {
                return null;
            }
            if (sps_data.chroma_format_idc < kChromaFormatIdcMin ||
                sps_data.chroma_format_idc > kChromaFormatIdcMax)
            {
#if DEBUG
                //fprintf(stderr, "invalid chroma_format_idc: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", sps_data.chroma_format_idc, kChromaFormatIdcMin, kChromaFormatIdcMax);
#endif  // FPRINT_ERRORS
                return null;
            }

            if (sps_data.chroma_format_idc == 3)
            {
                // separate_colour_plane_flag  u(1)
                if (!bit_buffer.ReadBits(1, out sps_data.separate_colour_plane_flag))
                {
                    return null;
                }
            }

            // bit_depth_luma_minus8  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out sps_data.bit_depth_luma_minus8))
            {
                return null;
            }
            if (sps_data.bit_depth_luma_minus8 < kBitDepthLumaMinus8Min ||
                sps_data.bit_depth_luma_minus8 > kBitDepthLumaMinus8Max)
            {
#if DEBUG
                //fprintf(stderr, "invalid bit_depth_luma_minus8: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", sps_data.bit_depth_luma_minus8, kBitDepthLumaMinus8Min, kBitDepthLumaMinus8Max);
#endif  // FPRINT_ERRORS
                return null;
            }

            // bit_depth_chroma_minus8  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out sps_data.bit_depth_chroma_minus8))
            {
                return null;
            }
            if (sps_data.bit_depth_chroma_minus8 < kBitDepthChromaMinus8Min ||
                sps_data.bit_depth_chroma_minus8 > kBitDepthChromaMinus8Max)
            {
#if DEBUG
                //fprintf(stderr, "invalid bit_depth_chroma_minus8: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", sps_data.bit_depth_chroma_minus8, kBitDepthChromaMinus8Min, kBitDepthChromaMinus8Max);
#endif  // FPRINT_ERRORS
                return null;
            }

            // qpprime_y_zero_transform_bypass_flag  u(1)
            if (!bit_buffer.ReadBits(1, out sps_data.qpprime_y_zero_transform_bypass_flag))
            {
                return null;
            }

            // seq_scaling_matrix_present_flag  u(1)
            if (!bit_buffer.ReadBits(1, out sps_data.seq_scaling_matrix_present_flag))
            {
                return null;
            }

            if (sps_data.seq_scaling_matrix_present_flag != 0)
            {
                for (UInt32 i = 0; i < ((sps_data.chroma_format_idc != 3) ? 8 : 12);
                     i++)
                {
                    // seq_scaling_list_present_flag[i]  u(1)
                    if (!bit_buffer.ReadBits(1, out bits_tmp))
                    {
                        return null;
                    }
                    sps_data.seq_scaling_list_present_flag.Add(bits_tmp);

                    if (sps_data.seq_scaling_list_present_flag[(int)i] != 0)
                    {
                        // scaling_list()
                        if (i < 6)
                        {
                            sps_data.scaling_list(
                                bit_buffer, i, sps_data.ScalingList4x4, 16,
                                sps_data.UseDefaultScalingMatrix4x4Flag);
                        }
                        else
                        {
                            sps_data.scaling_list(
                                bit_buffer, i - 6, sps_data.ScalingList8x8, 64,
                                sps_data.UseDefaultScalingMatrix4x4Flag);
                        }
                    }
                }
            }
        }

        // log2_max_frame_num_minus4  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out sps_data.log2_max_frame_num_minus4))
        {
            return null;
        }
        if (sps_data.log2_max_frame_num_minus4 < kLog2MaxFrameNumMinus4Min ||
            sps_data.log2_max_frame_num_minus4 > kLog2MaxFrameNumMinus4Max)
        {
#if DEBUG
            //fprintf(stderr, "invalid log2_max_frame_num_minus4: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", sps_data.log2_max_frame_num_minus4, kLog2MaxFrameNumMinus4Min, kLog2MaxFrameNumMinus4Max);
#endif  // FPRINT_ERRORS
            return null;
        }

        // pic_order_cnt_type  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out sps_data.pic_order_cnt_type))
        {
            return null;
        }
        if (sps_data.pic_order_cnt_type < kPicOrderCntTypeMin ||
            sps_data.pic_order_cnt_type > kPicOrderCntTypeMax)
        {
#if DEBUG
            //fprintf(stderr, "invalid pic_order_cnt_type: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", sps_data.pic_order_cnt_type, kPicOrderCntTypeMin, kPicOrderCntTypeMax);
#endif  // FPRINT_ERRORS
            return null;
        }

        if (sps_data.pic_order_cnt_type == 0)
        {
            // log2_max_pic_order_cnt_lsb_minus4  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out sps_data.log2_max_pic_order_cnt_lsb_minus4))
            {
                return null;
            }
            if (sps_data.log2_max_pic_order_cnt_lsb_minus4 <
                    kLog2MaxPicOrderCntLsbMinus4Min ||
                sps_data.log2_max_pic_order_cnt_lsb_minus4 >
                    kLog2MaxPicOrderCntLsbMinus4Max)
            {
#if DEBUG
                //fprintf(stderr, "invalid log2_max_pic_order_cnt_lsb_minus4: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", sps_data.log2_max_pic_order_cnt_lsb_minus4, kLog2MaxPicOrderCntLsbMinus4Min, kLog2MaxPicOrderCntLsbMinus4Max);
#endif  // FPRINT_ERRORS
                return null;
            }

        }
        else if (sps_data.pic_order_cnt_type == 1)
        {
            // delta_pic_order_always_zero_flag  u(1)
            if (!bit_buffer.ReadBits(1, out sps_data.delta_pic_order_always_zero_flag))
            {
                return null;
            }

            // offset_for_non_ref_pic  se(v)
            if (!bit_buffer.ReadSignedExponentialGolomb(out sps_data.offset_for_non_ref_pic))
            {
                return null;
            }

            // offset_for_top_to_bottom_field  se(v)
            if (!bit_buffer.ReadSignedExponentialGolomb(out sps_data.offset_for_top_to_bottom_field))
            {
                return null;
            }

            // num_ref_frames_in_pic_order_cnt_cycle  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out sps_data.num_ref_frames_in_pic_order_cnt_cycle))
            {
                return null;
            }
            if (sps_data.num_ref_frames_in_pic_order_cnt_cycle <
                    kNumRefFramesInPicOrderCntCycleMin ||
                sps_data.num_ref_frames_in_pic_order_cnt_cycle >
                    kNumRefFramesInPicOrderCntCycleMax)
            {
#if DEBUG
                //fprintf(stderr, "invalid num_ref_frames_in_pic_order_cnt_cycle: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", sps_data.num_ref_frames_in_pic_order_cnt_cycle, kNumRefFramesInPicOrderCntCycleMin, kNumRefFramesInPicOrderCntCycleMax);
#endif  // FPRINT_ERRORS
                return null;
            }

            for (UInt32 i = 0; i < sps_data.num_ref_frames_in_pic_order_cnt_cycle;
                 i++)
            {
                // offset_for_ref_frame[i]  se(v)
                if (!bit_buffer.ReadSignedExponentialGolomb(out golomb_tmp))
                {
                    return null;
                }
                sps_data.offset_for_ref_frame.Add(golomb_tmp);
            }
        }

        // max_num_ref_frames  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out sps_data.max_num_ref_frames))
        {
            return null;
        }
        if (sps_data.max_num_ref_frames > H264VuiParametersParser.kMaxDpbFrames)
        {
#if DEBUG
            //fprintf(stderr, "invalid max_num_ref_frames: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", sps_data.max_num_ref_frames, 0, H264VuiParametersParser::kMaxDpbFrames);
#endif  // FPRINT_ERRORS
            return null;
        }

        // gaps_in_frame_num_value_allowed_flag  u(1)
        if (!bit_buffer.ReadBits(1, out sps_data.gaps_in_frame_num_value_allowed_flag))
        {
            return null;
        }

        // pic_width_in_mbs_minus1  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out sps_data.pic_width_in_mbs_minus1))
        {
            return null;
        }
        if (sps_data.pic_width_in_mbs_minus1 > H264Common.kMaxMbWidth)
        {
#if DEBUG
            //fprintf(stderr, "invalid pic_width_in_mbs_minus1: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", sps_data.pic_width_in_mbs_minus1, 0, kMaxMbWidth);
#endif  // FPRINT_ERRORS
            return null;
        }

        // pic_height_in_map_units_minus1  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out sps_data.pic_height_in_map_units_minus1))
        {
            return null;
        }
        if (sps_data.pic_height_in_map_units_minus1 > H264Common.kMaxMbHeight)
        {
#if DEBUG
            //fprintf(stderr, "invalid pic_height_in_map_units_minus1: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", sps_data.pic_height_in_map_units_minus1, 0, kMaxMbHeight);
#endif  // FPRINT_ERRORS
            return null;
        }

        // frame_mbs_only_flag  u(1)
        if (!bit_buffer.ReadBits(1, out sps_data.frame_mbs_only_flag))
        {
            return null;
        }

        if (sps_data.frame_mbs_only_flag == 0)
        {
            // mb_adaptive_frame_field_flag  u(1)
            if (!bit_buffer.ReadBits(1, out sps_data.mb_adaptive_frame_field_flag))
            {
                return null;
            }
        }

        // direct_8x8_inference_flag  u(1)
        if (!bit_buffer.ReadBits(1, out sps_data.direct_8x8_inference_flag))
        {
            return null;
        }

        // frame_cropping_flag  u(1)
        if (!bit_buffer.ReadBits(1, out sps_data.frame_cropping_flag))
        {
            return null;
        }

        if (sps_data.frame_cropping_flag != 0)
        {
            // frame_crop_left_offset  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out sps_data.frame_crop_left_offset))
            {
                return null;
            }
            if (sps_data.frame_crop_left_offset > H264Common.kMaxWidth)
            {
#if DEBUG
                //fprintf(stderr, "invalid frame_crop_left_offset: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", sps_data.frame_crop_left_offset, 0, kMaxWidth);
#endif  // FPRINT_ERRORS
                return null;
            }

            // frame_crop_right_offset  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out sps_data.frame_crop_right_offset))
            {
                return null;
            }
            if (sps_data.frame_crop_right_offset > H264Common.kMaxWidth)
            {
#if DEBUG
                //fprintf(stderr, "invalid frame_crop_right_offset: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", sps_data.frame_crop_right_offset, 0, kMaxWidth);
#endif  // FPRINT_ERRORS
                return null;
            }

            // frame_crop_top_offset  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out sps_data.frame_crop_top_offset))
            {
                return null;
            }
            if (sps_data.frame_crop_top_offset > H264Common.kMaxHeight)
            {
#if DEBUG
                //fprintf(stderr, "invalid frame_crop_top_offset: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", sps_data.frame_crop_top_offset, 0, kMaxHeight);
#endif  // FPRINT_ERRORS
                return null;
            }

            // frame_crop_bottom_offset  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out sps_data.frame_crop_bottom_offset))
            {
                return null;
            }
            if (sps_data.frame_crop_bottom_offset > H264Common.kMaxHeight)
            {
#if DEBUG
                //fprintf(stderr, "invalid frame_crop_bottom_offset: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", sps_data.frame_crop_bottom_offset, 0, kMaxHeight);
#endif  // FPRINT_ERRORS
                return null;
            }
        }

        // vui_parameters_present_flag  u(1)
        if (!bit_buffer.ReadBits(1, out (sps_data.vui_parameters_present_flag)))
        {
            return null;
        }

        if (sps_data.vui_parameters_present_flag != 0)
        {
            // vui_parameters()
            sps_data.vui_parameters = H264VuiParametersParser.ParseVuiParameters(bit_buffer);
            if (sps_data.vui_parameters == null)
            {
                return null;
            }
        }

        return sps_data;
    }
}