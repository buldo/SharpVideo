using System;

namespace SharpVideo.H264;

/// <summary>
/// A class for parsing out a slice header data from an H264 NALU.
/// </summary>
public class H264SliceHeaderParser
{
    // Section 7.4.2.2: "The value of pic_parameter_set_id shall be in the
    // range of 0 to 255, inclusive."
    const uint32_t kPicParameterSetIdMin = 0;
    const uint32_t kPicParameterSetIdMax = 255;
    // Table 7-6 lists possible values for slice_type (inclusive).
    const uint32_t kSliceTypeMin = 0;
    const uint32_t kSliceTypeMax = 9;
    // Section 7.4.3: "The value of idr_pic_id shall be in the range of
    // 0 to 65535, inclusive."
    const uint32_t kIdrPicIdMin = 0;
    const uint32_t kIdrPicIdMax = 65535;
    // Section 7.4.3: "The value of redundant_pic_cnt shall be in the range of
    // 0 to 127, inclusive."
    const uint32_t kRedundantPicCntMin = 0;
    const uint32_t kRedundantPicCntMax = 127;
    // Section 7.4.3: "num_ref_idx_l0_active_minus1 shall be in the range of
    // 0 to 31, inclusive."
    const uint32_t kNumRefIdxL0ActiveMinux1Min = 0;
    const uint32_t kNumRefIdxL0ActiveMinux1Max = 31;
    // Section 7.4.3: "num_ref_idx_l1_active_minus1 shall be in the range of
    // 0 to 31, inclusive."
    const uint32_t kNumRefIdxL1ActiveMinux1Min = 0;
    const uint32_t kNumRefIdxL1ActiveMinux1Max = 31;
    // Section 7.4.3: "The value of cabac_init_idc shall be in the range of
    // 0 to 2, inclusive."
    const uint32_t kCabacInitIdcMin = 0;
    const uint32_t kCabacInitIdcMax = 2;
    // Section 7.4.3: "The value of disable_deblocking_filter_idc shall be in
    // the range of 0 to 2, inclusive."
    const uint32_t kDisableDeblockingFilterIdcMin = 0;
    const uint32_t kDisableDeblockingFilterIdcMax = 2;

    // Unpack RBSP and parse slice state from the supplied buffer.
    static SliceHeaderState? ParseSliceHeader(ReadOnlySpan<byte> data, uint32_t nal_ref_idc, uint32_t nal_unit_type,H264BitstreamParserState bitstream_parser_state)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new(unpacked_buffer.ToArray());
        return ParseSliceHeader(
            bit_buffer,
            nal_ref_idc,
            nal_unit_type,
            bitstream_parser_state);
    }

    static SliceHeaderState? ParseSliceHeader(BitBuffer bit_buffer, uint32_t nal_ref_idc, uint32_t nal_unit_type, H264BitstreamParserState bitstream_parser_state)
    {
        int32_t sgolomb_tmp;

        // H264 slice header (slice_header()) NAL Unit.
        // Section 7.3.3 ("Slice Header syntax") of the H.264
        // standard for a complete description.
        var slice_header = new SliceHeaderState();

        // input parameters
        slice_header.nal_ref_idc = nal_ref_idc;
        slice_header.nal_unit_type = nal_unit_type;

        // first_mb_in_slice  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out slice_header.first_mb_in_slice))
        {
            return null;
        }
        if (slice_header.first_mb_in_slice > (H264Common.kMaxMbPicSize - 1))
        {
#if DEBUG
            //fprintf(stderr, "invalid first_mb_in_slice: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", slice_header.first_mb_in_slice, 0, (kMaxMbPicSize - 1));
#endif  // FPRINT_ERRORS
            return null;
        }

        // slice_type  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out slice_header.slice_type))
        {
            return null;
        }
        if (slice_header.slice_type < kSliceTypeMin ||
            slice_header.slice_type > kSliceTypeMax)
        {
#if DEBUG
            //fprintf(stderr, "invalid slice_type: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", slice_header.slice_type, kSliceTypeMin, kSliceTypeMax);
#endif  // FPRINT_ERRORS
            return null;
        }

        // pic_parameter_set_id  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out slice_header.pic_parameter_set_id))
        {
            return null;
        }
        if (slice_header.pic_parameter_set_id < kPicParameterSetIdMin ||
            slice_header.pic_parameter_set_id > kPicParameterSetIdMax)
        {
#if DEBUG
            //fprintf(stderr, "invalid pic_parameter_set_id: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", slice_header.pic_parameter_set_id, kPicParameterSetIdMin, kPicParameterSetIdMax);
#endif  // FPRINT_ERRORS
            return null;
        }

        // get pps_id and sps_id and check their existence
        uint32_t pps_id = slice_header.pic_parameter_set_id;
        if (!bitstream_parser_state.pps.ContainsKey(pps_id))
        {
            // non-existent PPS id
#if DEBUG
            //fprintf(stderr, "non-existent PPS id: %u\n", pps_id);
#endif  // FPRINT_ERRORS
            return null;
        }
        var pps = bitstream_parser_state.pps[pps_id];

        uint32_t sps_id = pps.seq_parameter_set_id;
        if (!bitstream_parser_state.sps.ContainsKey(sps_id))
        {
            // non-existent SPS id
#if DEBUG
            //fprintf(stderr, "non-existent SPS id: %u\n", sps_id);
#endif  // FPRINT_ERRORS
            return null;
        }
        var sps = bitstream_parser_state.sps[sps_id];
        var sps_data = sps.sps_data;

        slice_header.separate_colour_plane_flag = sps_data.separate_colour_plane_flag;
        if (slice_header.separate_colour_plane_flag != 0)
        {
            // colour_plane_id  u(2)
            if (!bit_buffer.ReadBits(2, out slice_header.colour_plane_id))
            {
                return null;
            }
        }

        // frame_num  u(v)
        slice_header.log2_max_frame_num_minus4 = sps_data.log2_max_frame_num_minus4;
        uint32_t frame_num_len = SliceHeaderState.getFrameNumLen(slice_header.log2_max_frame_num_minus4);
        if (!bit_buffer.ReadBits((int)frame_num_len, out slice_header.frame_num))
        {
            return null;
        }

        slice_header.frame_mbs_only_flag = sps_data.frame_mbs_only_flag;
        if (slice_header.frame_mbs_only_flag == 0)
        {
            // field_pic_flag  u(1)
            if (!bit_buffer.ReadBits(1, out slice_header.field_pic_flag))
            {
                return null;
            }
            if (slice_header.field_pic_flag !=0 )
            {
                // bottom_field_flag  u(1)
                if (!bit_buffer.ReadBits(1, out slice_header.bottom_field_flag))
                {
                    return null;
                }
            }
        }

        var IdrPicFlag = ((slice_header.nal_unit_type == 5) ? true : false);
        if (IdrPicFlag)
        {
            // idr_pic_id  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out slice_header.idr_pic_id))
            {
                return null;
            }
            if (slice_header.idr_pic_id < kIdrPicIdMin ||
                slice_header.idr_pic_id > kIdrPicIdMax)
            {
#if DEBUG
                //fprintf(stderr, "invalid idr_pic_id: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", slice_header.idr_pic_id, kIdrPicIdMin, kIdrPicIdMax);
#endif  // FPRINT_ERRORS
                return null;
            }
        }

        slice_header.pic_order_cnt_type = sps_data.pic_order_cnt_type;
        if (slice_header.pic_order_cnt_type == 0)
        {
            uint32_t log2_max_pic_order_cnt_lsb_minus4 = sps_data.log2_max_pic_order_cnt_lsb_minus4;
            // pic_order_cnt_lsb  u(v)
            uint32_t pic_order_cnt_lsb_len = SliceHeaderState.getPicOrderCntLsbLen(log2_max_pic_order_cnt_lsb_minus4);
            if (!bit_buffer.ReadBits((int)pic_order_cnt_lsb_len, out slice_header.pic_order_cnt_lsb))
            {
                return null;
            }

            slice_header.bottom_field_pic_order_in_frame_present_flag = pps.bottom_field_pic_order_in_frame_present_flag;
            if (slice_header.bottom_field_pic_order_in_frame_present_flag != 1 && slice_header.field_pic_flag == 0)
            {
                // delta_pic_order_cnt_bottom  se(v)
                if (!bit_buffer.ReadSignedExponentialGolomb(out slice_header.delta_pic_order_cnt_bottom))
                {
                    return null;
                }
            }
        }

        slice_header.delta_pic_order_always_zero_flag = sps_data.delta_pic_order_always_zero_flag;
        if ((slice_header.pic_order_cnt_type == 1) && (slice_header.delta_pic_order_always_zero_flag == 0))
        {
            // delta_pic_order_cnt[0]  se(v)
            if (!bit_buffer.ReadSignedExponentialGolomb(out sgolomb_tmp))
            {
                return null;
            }
            slice_header.delta_pic_order_cnt.Add(sgolomb_tmp);

            if (slice_header.bottom_field_pic_order_in_frame_present_flag != 0 && slice_header.field_pic_flag == 0)
            {
                // delta_pic_order_cnt[1]  se(v)
                if (!bit_buffer.ReadSignedExponentialGolomb(out sgolomb_tmp))
                {
                    return null;
                }
                slice_header.delta_pic_order_cnt.Add(sgolomb_tmp);
            }
        }

        slice_header.redundant_pic_cnt_present_flag = pps.redundant_pic_cnt_present_flag;
        if (slice_header.redundant_pic_cnt_present_flag != 0)
        {
            // redundant_pic_cnt  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out slice_header.redundant_pic_cnt))
            {
                return null;
            }
            if (slice_header.redundant_pic_cnt < kRedundantPicCntMin ||
                slice_header.redundant_pic_cnt > kRedundantPicCntMax)
            {
#if DEBUG
                //fprintf(stderr, "invalid redundant_pic_cnt: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", slice_header.redundant_pic_cnt, kRedundantPicCntMin, kRedundantPicCntMax);
#endif  // FPRINT_ERRORS
                return null;
            }
        }

        if ((slice_header.slice_type == (uint)SliceType.B) || (slice_header.slice_type == (uint)SliceType.B_ALL))
        {  // slice_type == B
           // direct_spatial_mv_pred_flag  u(1)
            if (!bit_buffer.ReadBits(1, out slice_header.direct_spatial_mv_pred_flag))
            {
                return null;
            }
        }

        if ((slice_header.slice_type == (uint)SliceType.P) ||
            (slice_header.slice_type == (uint)SliceType.P_ALL) ||
            (slice_header.slice_type == (uint)SliceType.SP) ||
            (slice_header.slice_type == (uint)SliceType.SP_ALL) ||
            (slice_header.slice_type == (uint)SliceType.B) ||
            (slice_header.slice_type == (uint)SliceType.B_ALL))
        {  // slice_type == P || slice_type == SP ||
           // slice_type == B
           // num_ref_idx_active_override_flag  u(1)
            if (!bit_buffer.ReadBits(1, out slice_header.num_ref_idx_active_override_flag))
            {
                return null;
            }

            if (slice_header.num_ref_idx_active_override_flag != 0)
            {
                // num_ref_idx_l0_active_minus1  ue(v)
                if (!bit_buffer.ReadExponentialGolomb(out slice_header.num_ref_idx_l0_active_minus1))
                {
                    return null;
                }
                if (slice_header.num_ref_idx_l0_active_minus1 < kNumRefIdxL0ActiveMinux1Min ||
                    slice_header.num_ref_idx_l0_active_minus1 > kNumRefIdxL0ActiveMinux1Max)
                {
#if DEBUG
                    //fprintf(stderr, "invalid num_ref_idx_l0_active_minus1: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", slice_header.num_ref_idx_l0_active_minus1, kNumRefIdxL0ActiveMinux1Min, kNumRefIdxL0ActiveMinux1Max);
#endif  // FPRINT_ERRORS
                    return null;
                }

                if ((slice_header.slice_type == (uint)SliceType.B) ||
                    (slice_header.slice_type == (uint)SliceType.B_ALL))
                {  // slice_type == B
                   // num_ref_idx_l1_active_minus1  ue(v)
                    if (!bit_buffer.ReadExponentialGolomb(out slice_header.num_ref_idx_l1_active_minus1))
                    {
                        return null;
                    }
                    if (slice_header.num_ref_idx_l1_active_minus1 < kNumRefIdxL1ActiveMinux1Min ||
                        slice_header.num_ref_idx_l1_active_minus1 > kNumRefIdxL1ActiveMinux1Max)
                    {
#if DEBUG
                        //fprintf(stderr, "invalid num_ref_idx_l1_active_minus1: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", slice_header.num_ref_idx_l1_active_minus1, kNumRefIdxL1ActiveMinux1Min, kNumRefIdxL1ActiveMinux1Max);
#endif  // FPRINT_ERRORS
                        return null;
                    }
                }
            }
            else
            {
                // use the default values from the PPS
                slice_header.num_ref_idx_l0_active_minus1 = pps.num_ref_idx_l0_default_active_minus1;
                slice_header.num_ref_idx_l1_active_minus1 = pps.num_ref_idx_l1_default_active_minus1;
            }
        }

        if (slice_header.nal_unit_type == 20 || slice_header.nal_unit_type == 21)
        {
            // ref_pic_list_mvc_modification()
#if DEBUG
            // TODO(chemag): add support for ref_pic_list_mvc_modification()
            //fprintf(stderr, "error: unimplemented ref_pic_list_mvc_modification in pps\n");
#endif  // FPRINT_ERRORS
        }
        else
        {
            // ref_pic_list_modification(slice_type)
            slice_header.ref_pic_list_modification = H264RefPicListModificationParser.ParseRefPicListModification(bit_buffer, slice_header.slice_type);
            if (slice_header.ref_pic_list_modification == null)
            {
                return null;
            }
        }

        slice_header.weighted_pred_flag = pps.weighted_pred_flag;
        slice_header.weighted_bipred_idc = pps.weighted_bipred_idc;

        if ((slice_header.weighted_pred_flag != 0 &&
             ((slice_header.slice_type == (uint)SliceType.P) ||
              (slice_header.slice_type == (uint)SliceType.P_ALL) ||
              (slice_header.slice_type == (uint)SliceType.SP) ||
              (slice_header.slice_type == (uint)SliceType.SP_ALL))) ||
            ((slice_header.weighted_bipred_idc == 1) &&
             ((slice_header.slice_type == (uint)SliceType.B) ||
              (slice_header.slice_type == (uint)SliceType.B_ALL))))
        {
            // pred_weight_table(slice_type, num_ref_idx_l0_active_minus1,
            // num_ref_idx_l1_active_minus1)
            uint32_t ChromaArrayType = sps_data.getChromaArrayType();
            slice_header.pred_weight_table =
                H264PredWeightTableParser.ParsePredWeightTable(
                    bit_buffer, ChromaArrayType, slice_header.slice_type,
                    slice_header.num_ref_idx_l0_active_minus1,
                    slice_header.num_ref_idx_l1_active_minus1);
            if (slice_header.pred_weight_table == null)
            {
                return null;
            }
        }

        if (slice_header.nal_ref_idc != 0)
        {
            // dec_ref_pic_marking(nal_unit_type)
            slice_header.dec_ref_pic_marking = H264DecRefPicMarkingParser.ParseDecRefPicMarking(bit_buffer, slice_header.nal_unit_type);
            if (slice_header.dec_ref_pic_marking == null)
            {
                return null;
            }
        }

        slice_header.entropy_coding_mode_flag = pps.entropy_coding_mode_flag;
        if (slice_header.entropy_coding_mode_flag != 0 &&
            (slice_header.slice_type != (uint)SliceType.I) &&
            (slice_header.slice_type != (uint)SliceType.I_ALL) &&
            (slice_header.slice_type != (uint)SliceType.SI) &&
            (slice_header.slice_type != (uint)SliceType.SI_ALL))
        {
            // cabac_init_idc  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out slice_header.cabac_init_idc))
            {
                return null;
            }
            if (slice_header.cabac_init_idc < kCabacInitIdcMin ||
                slice_header.cabac_init_idc > kCabacInitIdcMax)
            {
#if DEBUG
                //fprintf(stderr, "invalid cabac_init_idc: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", slice_header.cabac_init_idc, kCabacInitIdcMin, kCabacInitIdcMax);
#endif  // FPRINT_ERRORS
                return null;
            }
        }

        // slice_qp_delta  se(v)
        if (!bit_buffer.ReadSignedExponentialGolomb(out slice_header.slice_qp_delta))
        {
            return null;
        }
        if (slice_header.slice_qp_delta < (-51 - 6 * (int32_t)(sps_data.bit_depth_luma_minus8)) ||
            slice_header.slice_qp_delta > (51 + 6 * (int32_t)(sps_data.bit_depth_luma_minus8)))
        {
#if DEBUG
            //fprintf(stderr, "invalid slice_qp_delta: %" PRIi32" not in range ""[%" PRIi32 ", %" PRIi32 "]\n", slice_header.slice_qp_delta, -51 - 6 * static_cast<int32_t>(sps_data.bit_depth_luma_minus8), 51 + 6 * static_cast<int32_t>(sps_data.bit_depth_luma_minus8));
#endif  // FPRINT_ERRORS
            return null;
        }

        if ((slice_header.slice_type == (uint)SliceType.SP) ||
            (slice_header.slice_type == (uint)SliceType.SP_ALL) ||
            (slice_header.slice_type == (uint)SliceType.SI) ||
            (slice_header.slice_type == (uint)SliceType.SI_ALL))
        {
            if ((slice_header.slice_type == (uint)SliceType.SP) ||
                (slice_header.slice_type == (uint)SliceType.SP_ALL))
            {
                // sp_for_switch_flag  u(1)
                if (!bit_buffer.ReadBits(1, out slice_header.sp_for_switch_flag))
                {
                    return null;
                }
            }

            // slice_qs_delta  se(v)
            if (!bit_buffer.ReadSignedExponentialGolomb(out slice_header.slice_qs_delta))
            {
                return null;
            }
        }

        slice_header.deblocking_filter_control_present_flag = pps.deblocking_filter_control_present_flag;
        if (slice_header.deblocking_filter_control_present_flag != 0)
        {
            // disable_deblocking_filter_idc  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out slice_header.disable_deblocking_filter_idc))
            {
                return null;
            }
            if (slice_header.disable_deblocking_filter_idc < kDisableDeblockingFilterIdcMin ||
                slice_header.disable_deblocking_filter_idc > kDisableDeblockingFilterIdcMax)
            {
#if DEBUG
                //fprintf(stderr, "invalid disable_deblocking_filter_idc: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", slice_header.disable_deblocking_filter_idc, kDisableDeblockingFilterIdcMin, kDisableDeblockingFilterIdcMax);
#endif  // FPRINT_ERRORS
                return null;
            }

            if (slice_header.disable_deblocking_filter_idc != 1)
            {
                // slice_alpha_c0_offset_div2  se(v)
                if (!bit_buffer.ReadSignedExponentialGolomb(out slice_header.slice_alpha_c0_offset_div2))
                {
                    return null;
                }

                // slice_beta_offset_div2  se(v)
                if (!bit_buffer.ReadSignedExponentialGolomb(out slice_header.slice_beta_offset_div2))
                {
                    return null;
                }
            }
        }

        slice_header.num_slice_groups_minus1 = pps.num_slice_groups_minus1;
        slice_header.slice_group_map_type = pps.slice_group_map_type;
        if ((slice_header.num_slice_groups_minus1 > 0) &&
            (slice_header.slice_group_map_type >= 3) &&
            (slice_header.slice_group_map_type <= 5))
        {
            // slice_group_change_cycle  u(v)
            slice_header.pic_width_in_mbs_minus1 = sps_data.pic_width_in_mbs_minus1;
            slice_header.pic_height_in_map_units_minus1 = sps_data.pic_height_in_map_units_minus1;
            slice_header.slice_group_change_rate_minus1 = pps.slice_group_change_rate_minus1;
            uint32_t slice_group_change_cycle_len = SliceHeaderState.getSliceGroupChangeCycleLen(
                slice_header.pic_width_in_mbs_minus1,
                slice_header.pic_height_in_map_units_minus1,
                slice_header.slice_group_change_rate_minus1);
            // Rec. ITU-T H.264 (2012) Page 67, Section 7.4.3
            if (!bit_buffer.ReadBits((int)slice_group_change_cycle_len, out slice_header.slice_group_change_cycle))
            {
                return null;
            }
        }

        return slice_header;
    }
}