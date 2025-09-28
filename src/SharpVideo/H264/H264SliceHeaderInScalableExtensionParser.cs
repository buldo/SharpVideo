namespace SharpVideo.H264;

class H264SliceHeaderInScalableExtensionParser
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
    public static SliceHeaderInScalableExtensionState ParseSliceHeaderInScalableExtension(ReadOnlySpan<byte> data,
        NalUnitHeaderState nal_unit_header, H264BitstreamParserState bitstream_parser_state)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new(unpacked_buffer.ToArray());
        return ParseSliceHeaderInScalableExtension(bit_buffer, nal_unit_header, bitstream_parser_state);
    }

    public static SliceHeaderInScalableExtensionState? ParseSliceHeaderInScalableExtension(BitBuffer bit_buffer,
        NalUnitHeaderState nal_unit_header, H264BitstreamParserState bitstream_parser_state)
    {
        int32_t sgolomb_tmp;

        // H264 slice header (slice_header_in_scalable_extension()) NAL Unit.
        // Section G.7.3.3.4 ("Slice header in scalable extension syntax") of
        // the H.264 standard for a complete description.
        var shise = new SliceHeaderInScalableExtensionState();

        // input parameters
        shise.nal_ref_idc = nal_unit_header.nal_ref_idc;
        shise.nal_unit_type = nal_unit_header.nal_unit_type;

        // first_mb_in_slice  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out shise.first_mb_in_slice))
        {
            return null;
        }

        if (shise.first_mb_in_slice > (H264Common.kMaxMbPicSize - 1))
        {
#if DEBUG
            //fprintf(stderr, "invalid first_mb_in_slice: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", shise.first_mb_in_slice, 0, (kMaxMbPicSize - 1));
#endif // FPRINT_ERRORS
            return null;
        }

        // slice_type  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out shise.slice_type))
        {
            return null;
        }

        if (shise.slice_type < kSliceTypeMin || shise.slice_type > kSliceTypeMax)
        {
#if DEBUG
            //fprintf(stderr, "invalid slice_type: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", shise.slice_type, kSliceTypeMin, kSliceTypeMax);
#endif // FPRINT_ERRORS
            return null;
        }

        // pic_parameter_set_id  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out shise.pic_parameter_set_id))
        {
            return null;
        }

        if (shise.pic_parameter_set_id < kPicParameterSetIdMin ||
            shise.pic_parameter_set_id > kPicParameterSetIdMax)
        {
#if DEBUG
            //fprintf(stderr, "invalid pic_parameter_set_id: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", shise.pic_parameter_set_id, kPicParameterSetIdMin, kPicParameterSetIdMax);
#endif // FPRINT_ERRORS
            return null;
        }

        // get pps_id, sps_id, and subset_sps_id and check their existence
        uint32_t pps_id = shise.pic_parameter_set_id;
        if (!bitstream_parser_state.pps.ContainsKey(pps_id))
        {
            // non-existent PPS id
#if DEBUG
            //fprintf(stderr, "non-existent PPS id: %u\n", pps_id);
#endif // FPRINT_ERRORS
            return null;
        }

        var pps = bitstream_parser_state.pps[pps_id];

        uint32_t sps_id = pps.seq_parameter_set_id;
        if (!bitstream_parser_state.sps.ContainsKey(sps_id))
        {
            // non-existent SPS id
#if DEBUG
            //fprintf(stderr, "non-existent SPS id: %u\n", sps_id);
#endif // FPRINT_ERRORS
            return null;
        }

        var sps = bitstream_parser_state.sps[sps_id];
        var sps_data = sps.sps_data;

        uint32_t subset_sps_id = pps.seq_parameter_set_id;
        if (!bitstream_parser_state.subset_sps.ContainsKey(subset_sps_id))
        {
            // non-existent SPS id
#if DEBUG
            //fprintf(stderr, "non-existent subset SPS id: %u\n", subset_sps_id);
#endif // FPRINT_ERRORS
            return null;
        }

        var subset_sps = bitstream_parser_state.subset_sps[subset_sps_id];
        var subset_sps_svc_extension = subset_sps.seq_parameter_set_svc_extension;
        if (subset_sps_svc_extension == null)
        {
            // slice_header_in_scalable_extension() (defined inside
            // slice_layer_extension_rbsp()) requires accessing
            // seq_parameter_set_svc_extension(() inside the subset SPS
            return null;
        }

        shise.separate_colour_plane_flag = sps_data.separate_colour_plane_flag;
        if (shise.separate_colour_plane_flag == 1)
        {
            // colour_plane_id  u(2)
            if (!bit_buffer.ReadBits(2, out shise.colour_plane_id))
            {
                return null;
            }
        }

        // frame_num  u(v)
        shise.log2_max_frame_num_minus4 = sps_data.log2_max_frame_num_minus4;
        uint32_t frame_num_len = SliceHeaderInScalableExtensionState.getFrameNumLen(shise.log2_max_frame_num_minus4);
        if (!bit_buffer.ReadBits((int)frame_num_len, out shise.frame_num))
        {
            return null;
        }

        shise.frame_mbs_only_flag = sps_data.frame_mbs_only_flag;
        if (shise.frame_mbs_only_flag == 0)
        {
            // field_pic_flag  u(1)
            if (!bit_buffer.ReadBits(1, out shise.field_pic_flag))
            {
                return null;
            }

            if (shise.field_pic_flag != 0)
            {
                // bottom_field_flag  u(1)
                if (!bit_buffer.ReadBits(1, out shise.bottom_field_flag))
                {
                    return null;
                }
            }
        }

        bool IdrPicFlag = (shise.nal_unit_type == 5);
        if (IdrPicFlag)
        {
            // idr_pic_id  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out shise.idr_pic_id))
            {
                return null;
            }

            if (shise.idr_pic_id < kIdrPicIdMin || shise.idr_pic_id > kIdrPicIdMax)
            {
#if DEBUG
                //fprintf(stderr,"invalid idr_pic_id: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", shise.idr_pic_id, kIdrPicIdMin, kIdrPicIdMax);
#endif // FPRINT_ERRORS
                return null;
            }
        }

        shise.pic_order_cnt_type = sps_data.pic_order_cnt_type;
        if (shise.pic_order_cnt_type == 0)
        {
            uint32_t log2_max_pic_order_cnt_lsb_minus4 =
                sps_data.log2_max_pic_order_cnt_lsb_minus4;
            // pic_order_cnt_lsb  u(v)
            uint32_t pic_order_cnt_lsb_len =
                SliceHeaderInScalableExtensionState.getPicOrderCntLsbLen(log2_max_pic_order_cnt_lsb_minus4);
            if (!bit_buffer.ReadBits((int)pic_order_cnt_lsb_len, out shise.pic_order_cnt_lsb))
            {
                return null;
            }

            shise.bottom_field_pic_order_in_frame_present_flag = pps.bottom_field_pic_order_in_frame_present_flag;
            if (shise.bottom_field_pic_order_in_frame_present_flag != 0 && shise.field_pic_flag == 0)
            {
                // delta_pic_order_cnt_bottom  se(v)
                if (!bit_buffer.ReadSignedExponentialGolomb(out shise.delta_pic_order_cnt_bottom))
                {
                    return null;
                }
            }
        }

        shise.delta_pic_order_always_zero_flag = sps_data.delta_pic_order_always_zero_flag;
        if ((shise.pic_order_cnt_type == 1) && (shise.delta_pic_order_always_zero_flag == 0))
        {
            // delta_pic_order_cnt[0]  se(v)
            if (!bit_buffer.ReadSignedExponentialGolomb(out sgolomb_tmp))
            {
                return null;
            }

            shise.delta_pic_order_cnt.Add(sgolomb_tmp);

            if (shise.bottom_field_pic_order_in_frame_present_flag != 0 && shise.field_pic_flag == 0)
            {
                // delta_pic_order_cnt[1]  se(v)
                if (!bit_buffer.ReadSignedExponentialGolomb(out sgolomb_tmp))
                {
                    return null;
                }

                shise.delta_pic_order_cnt.Add(sgolomb_tmp);
            }
        }

        shise.redundant_pic_cnt_present_flag = pps.redundant_pic_cnt_present_flag;
        if (shise.redundant_pic_cnt_present_flag != 0)
        {
            // redundant_pic_cnt  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out shise.redundant_pic_cnt))
            {
                return null;
            }

            if (shise.redundant_pic_cnt < kRedundantPicCntMin || shise.redundant_pic_cnt > kRedundantPicCntMax)
            {
#if DEBUG
                //fprintf(stderr, "invalid redundant_pic_cnt: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", shise.redundant_pic_cnt, kRedundantPicCntMin, kRedundantPicCntMax);
#endif // FPRINT_ERRORS
                return null;
            }
        }

        var nal_unit_header_svc_extension = nal_unit_header.nal_unit_header_svc_extension;
        if (nal_unit_header_svc_extension == null)
        {
            // non-existent nal_unit_header_svc_extension
            return null;
        }

        shise.quality_id = nal_unit_header_svc_extension.quality_id;
        if (shise.quality_id == 0)
        {
            if ((shise.slice_type == (uint)SvcSliceType.EBa) ||
                (shise.slice_type == (uint)SvcSliceType.EBb))
            {
                // slice_type == EB
                // direct_spatial_mv_pred_flag  u(1)
                if (!bit_buffer.ReadBits(1, out shise.direct_spatial_mv_pred_flag))
                {
                    return null;
                }
            }

            if ((shise.slice_type == (uint)SvcSliceType.EPa) ||
                (shise.slice_type == (uint)SvcSliceType.EPb) ||
                (shise.slice_type == (uint)SvcSliceType.EBa) ||
                (shise.slice_type == (uint)SvcSliceType.EBb))
            {
                // slice_type == EP || slice_type == EB
                // num_ref_idx_active_override_flag  u(1)
                if (!bit_buffer.ReadBits(1, out shise.num_ref_idx_active_override_flag))
                {
                    return null;
                }

                if (shise.num_ref_idx_active_override_flag != 0)
                {
                    // num_ref_idx_l0_active_minus1  ue(v)
                    if (!bit_buffer.ReadExponentialGolomb(out shise.num_ref_idx_l0_active_minus1))
                    {
                        return null;
                    }

                    if (shise.num_ref_idx_l0_active_minus1 < kNumRefIdxL0ActiveMinux1Min ||
                        shise.num_ref_idx_l0_active_minus1 > kNumRefIdxL0ActiveMinux1Max)
                    {
#if DEBUG
                        //fprintf(stderr, "invalid num_ref_idx_l0_active_minus1: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", shise.num_ref_idx_l0_active_minus1, kNumRefIdxL0ActiveMinux1Min, kNumRefIdxL0ActiveMinux1Max);
#endif // FPRINT_ERRORS
                        return null;
                    }

                    if ((shise.slice_type == (uint)SvcSliceType.EBa) ||
                        (shise.slice_type == (uint)SvcSliceType.EBb))
                    {
                        // slice_type == EB
                        // num_ref_idx_l1_active_minus1  ue(v)
                        if (!bit_buffer.ReadExponentialGolomb(out shise.num_ref_idx_l1_active_minus1))
                        {
                            return null;
                        }

                        if (shise.num_ref_idx_l1_active_minus1 <
                            kNumRefIdxL1ActiveMinux1Min ||
                            shise.num_ref_idx_l1_active_minus1 >
                            kNumRefIdxL1ActiveMinux1Max)
                        {
#if DEBUG
                            //fprintf(stderr, "invalid num_ref_idx_l1_active_minus1: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", shise.num_ref_idx_l1_active_minus1, kNumRefIdxL1ActiveMinux1Min, kNumRefIdxL1ActiveMinux1Max);
#endif // FPRINT_ERRORS
                            return null;
                        }
                    }
                }
            }

            // ref_pic_list_modification(slice_type)
            shise.ref_pic_list_modification = H264RefPicListModificationParser.ParseRefPicListModification(bit_buffer, shise.slice_type);
            if (shise.ref_pic_list_modification == null)
            {
                return null;
            }

            shise.weighted_pred_flag = pps.weighted_pred_flag;
            shise.weighted_bipred_idc = pps.weighted_bipred_idc;

            if ((shise.weighted_pred_flag !=0 &&
                 ((shise.slice_type == (uint)SvcSliceType.EPa) ||
                  (shise.slice_type == (uint)SvcSliceType.EPb))) ||
                ((shise.weighted_bipred_idc == 1) &&
                 ((shise.slice_type == (uint)SvcSliceType.EBa) ||
                  (shise.slice_type == (uint)SvcSliceType.EBb))))
            {
                shise.no_inter_layer_pred_flag =
                    nal_unit_header_svc_extension.no_inter_layer_pred_flag;
                if ( 0 ==shise.no_inter_layer_pred_flag)
                {
                    // base_pred_weight_table_flag  u(1)
                    if (!bit_buffer.ReadBits(1, out shise.base_pred_weight_table_flag))
                    {
                        return null;
                    }
                }

                if (shise.no_inter_layer_pred_flag != 0 || 0==shise.base_pred_weight_table_flag)
                {
                    // pred_weight_table(slice_type, num_ref_idx_l0_active_minus1,
                    // num_ref_idx_l1_active_minus1)
                    shise.ChromaArrayType = sps_data.getChromaArrayType();
                    shise.pred_weight_table =
                        H264PredWeightTableParser.ParsePredWeightTable(
                            bit_buffer, shise.ChromaArrayType, shise.slice_type,
                            shise.num_ref_idx_l0_active_minus1,
                            shise.num_ref_idx_l1_active_minus1);
                    if (shise.pred_weight_table == null)
                    {
                        return null;
                    }
                }
            }

            if (shise.nal_ref_idc != 0)
            {
                // dec_ref_pic_marking(nal_unit_type)
                shise.dec_ref_pic_marking =
                    H264DecRefPicMarkingParser.ParseDecRefPicMarking(
                        bit_buffer, shise.nal_unit_type);
                if (shise.dec_ref_pic_marking == null)
                {
                    return null;
                }

                shise.slice_header_restriction_flag =
                    subset_sps_svc_extension.slice_header_restriction_flag;
                if (0==shise.slice_header_restriction_flag)
                {
                    // store_ref_base_pic_flag  u(1)
                    if (!bit_buffer.ReadBits(1, out shise.store_ref_base_pic_flag))
                    {
                        return null;
                    }

                    shise.use_ref_base_pic_flag = nal_unit_header_svc_extension.use_ref_base_pic_flag;
                    shise.idr_flag = nal_unit_header_svc_extension.idr_flag;
                    if ((shise.use_ref_base_pic_flag != 0 || shise.store_ref_base_pic_flag!=0) && 0==shise.idr_flag)
                    {
                        // dec_ref_base_pic_marking()
#if DEBUG
                        //fprintf(stderr, "error: dec_ref_base_pic_marking undefined\n");
#endif // FPRINT_ERRORS
                        return null;
                    }
                }
            }
        }

        shise.entropy_coding_mode_flag = pps.entropy_coding_mode_flag;
        if (shise.entropy_coding_mode_flag !=0 &&
            (shise.slice_type != (uint)SvcSliceType.EIa) &&
            (shise.slice_type != (uint)SvcSliceType.EIb))
        {
            // cabac_init_idc  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out shise.cabac_init_idc))
            {
                return null;
            }

            if (shise.cabac_init_idc < kCabacInitIdcMin ||
                shise.cabac_init_idc > kCabacInitIdcMax)
            {
#if DEBUG
                //fprintf(stderr, "invalid cabac_init_idc: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", shise.cabac_init_idc, kCabacInitIdcMin, kCabacInitIdcMax);
#endif // FPRINT_ERRORS
                return null;
            }
        }

        // slice_qp_delta  se(v)
        if (!bit_buffer.ReadSignedExponentialGolomb(out shise.slice_qp_delta))
        {
            return null;
        }

        shise.deblocking_filter_control_present_flag = pps.deblocking_filter_control_present_flag;
        if (shise.deblocking_filter_control_present_flag !=0)
        {
            // disable_deblocking_filter_idc  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out shise.disable_deblocking_filter_idc))
            {
                return null;
            }

            if (shise.disable_deblocking_filter_idc < kDisableDeblockingFilterIdcMin ||
                shise.disable_deblocking_filter_idc > kDisableDeblockingFilterIdcMax)
            {
#if DEBUG
                //fprintf(stderr, "invalid disable_deblocking_filter_idc: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", shise.disable_deblocking_filter_idc, kDisableDeblockingFilterIdcMin, kDisableDeblockingFilterIdcMax);
#endif // FPRINT_ERRORS
                return null;
            }

            if (shise.disable_deblocking_filter_idc != 1)
            {
                // slice_alpha_c0_offset_div2  se(v)
                if (!bit_buffer.ReadSignedExponentialGolomb( out shise.slice_alpha_c0_offset_div2))
                {
                    return null;
                }

                // slice_beta_offset_div2  se(v)
                if (!bit_buffer.ReadSignedExponentialGolomb(out shise.slice_beta_offset_div2))
                {
                    return null;
                }
            }
        }

        shise.num_slice_groups_minus1 = pps.num_slice_groups_minus1;
        shise.slice_group_map_type = pps.slice_group_map_type;
        if ((shise.num_slice_groups_minus1 > 0) &&
            (shise.slice_group_map_type >= 3) &&
            (shise.slice_group_map_type <= 5))
        {
            // slice_group_change_cycle  u(v)
            shise.pic_width_in_mbs_minus1 = sps_data.pic_width_in_mbs_minus1;
            shise.pic_height_in_map_units_minus1 =
                sps_data.pic_height_in_map_units_minus1;
            shise.slice_group_change_rate_minus1 = pps.slice_group_change_rate_minus1;
            uint32_t slice_group_change_cycle_len = SliceHeaderInScalableExtensionState.getSliceGroupChangeCycleLen(
                shise.pic_width_in_mbs_minus1, shise.pic_height_in_map_units_minus1,
                shise.slice_group_change_rate_minus1);
            // Rec. ITU-T H.264 (2012) Page 67, Section 7.4.3
            if (!bit_buffer.ReadBits((int)slice_group_change_cycle_len, out shise.slice_group_change_cycle))
            {
                return null;
            }
        }

        if (shise.no_inter_layer_pred_flag == 0 && shise.quality_id == 0)
        {
            // ref_layer_dq_id  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out shise.ref_layer_dq_id))
            {
                return null;
            }

            shise.inter_layer_deblocking_filter_control_present_flag =
                subset_sps_svc_extension.inter_layer_deblocking_filter_control_present_flag;
            if (shise.inter_layer_deblocking_filter_control_present_flag != 0)
            {
                // disable_inter_layer_deblocking_filter_idc  ue(v)
                if (!bit_buffer.ReadExponentialGolomb(out shise.disable_inter_layer_deblocking_filter_idc))
                {
                    return null;
                }

                if (shise.disable_inter_layer_deblocking_filter_idc != 1)
                {
                    // inter_layer_slice_alpha_c0_offset_div2  se(v)
                    if (!bit_buffer.ReadSignedExponentialGolomb(out shise.inter_layer_slice_alpha_c0_offset_div2))
                    {
                        return null;
                    }

                    // inter_layer_slice_beta_offset_div2  se(v)
                    if (!bit_buffer.ReadSignedExponentialGolomb(out shise.inter_layer_slice_beta_offset_div2))
                    {
                        return null;
                    }
                }
            }

            // constrained_intra_resampling_flag  u(1)
            if (!bit_buffer.ReadBits(1, out shise.constrained_intra_resampling_flag))
            {
                return null;
            }

            shise.extended_spatial_scalability_idc =
                subset_sps_svc_extension.extended_spatial_scalability_idc;
            if (shise.extended_spatial_scalability_idc == 2)
            {
                shise.ChromaArrayType = sps_data.getChromaArrayType();
                if (shise.ChromaArrayType > 0)
                {
                    // ref_layer_chroma_phase_x_plus1_flag  u(1)
                    if (!bit_buffer.ReadBits(1, out shise.ref_layer_chroma_phase_x_plus1_flag))
                    {
                        return null;
                    }

                    // ref_layer_chroma_phase_y_plus1  u(2)
                    if (!bit_buffer.ReadBits(2, out shise.ref_layer_chroma_phase_y_plus1))
                    {
                        return null;
                    }
                }

                // scaled_ref_layer_left_offset  se(v)
                if (!bit_buffer.ReadSignedExponentialGolomb(out shise.scaled_ref_layer_left_offset))
                {
                    return null;
                }

                // scaled_ref_layer_top_offset  se(v)
                if (!bit_buffer.ReadSignedExponentialGolomb(out shise.scaled_ref_layer_top_offset))
                {
                    return null;
                }

                // scaled_ref_layer_right_offset  se(v)
                if (!bit_buffer.ReadSignedExponentialGolomb(out shise.scaled_ref_layer_right_offset))
                {
                    return null;
                }

                // scaled_ref_layer_bottom_offset  se(v)
                if (!bit_buffer.ReadSignedExponentialGolomb(out shise.scaled_ref_layer_bottom_offset))
                {
                    return null;
                }
            }
        }

        if (shise.no_inter_layer_pred_flag == 0)
        {
            // slice_skip_flag  u(1)
            if (!bit_buffer.ReadBits(1, out shise.slice_skip_flag))
            {
                return null;
            }

            if (shise.slice_skip_flag != 0)
            {
                // num_mbs_in_slice_minus1  ue(v)
                if (!bit_buffer.ReadExponentialGolomb(out shise.num_mbs_in_slice_minus1))
                {
                    return null;
                }

            }
            else
            {
                // adaptive_base_mode_flag  u(1)
                if (!bit_buffer.ReadBits(1, out shise.adaptive_base_mode_flag))
                {
                    return null;
                }

                if (shise.adaptive_base_mode_flag == 0)
                {
                    // default_base_mode_flag  u(1)
                    if (!bit_buffer.ReadBits(1, out shise.default_base_mode_flag))
                    {
                        return null;
                    }
                }

                if (shise.default_base_mode_flag == 0)
                {
                    // adaptive_motion_prediction_flag  u(1)
                    if (!bit_buffer.ReadBits(1, out shise.adaptive_motion_prediction_flag))
                    {
                        return null;
                    }

                    if (shise.adaptive_motion_prediction_flag == 0)
                    {
                        // default_motion_prediction_flag  u(1)
                        if (!bit_buffer.ReadBits(1, out shise.default_motion_prediction_flag))
                        {
                            return null;
                        }
                    }
                }

                // adaptive_residual_prediction_flag  u(1)
                if (!bit_buffer.ReadBits(1, out shise.adaptive_residual_prediction_flag))
                {
                    return null;
                }

                if (shise.adaptive_residual_prediction_flag == 0)
                {
                    // default_residual_prediction_flag  u(1)
                    if (!bit_buffer.ReadBits(1, out shise.default_residual_prediction_flag))
                    {
                        return null;
                    }
                }
            }

            shise.adaptive_tcoeff_level_prediction_flag =
                subset_sps_svc_extension.adaptive_tcoeff_level_prediction_flag;
            if (shise.adaptive_tcoeff_level_prediction_flag != 0)
            {
                // tcoeff_level_prediction_flag  u(1)
                if (!bit_buffer.ReadBits(1, out shise.tcoeff_level_prediction_flag))
                {
                    return null;
                }
            }
        }

        if (shise.slice_header_restriction_flag == 0 && shise.slice_skip_flag == 0)
        {
            // scan_idx_start  u(4)
            if (!bit_buffer.ReadBits(4, out shise.scan_idx_start))
            {
                return null;
            }

            // scan_idx_end  u(4)
            if (!bit_buffer.ReadBits(4, out shise.scan_idx_end))
            {
                return null;
            }
        }

        return shise;
    }
}