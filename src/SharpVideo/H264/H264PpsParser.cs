using System.Diagnostics;

namespace SharpVideo.H264;


/// <summary>
/// A class for parsing out a video sequence parameter set (PPS) data from an H264 NALU.
/// </summary>
/// <remarks>
/// This is based off the 2012 version of the H.264 standard.
/// You can find it on this page: http://www.itu.int/rec/T-REC-H.264
/// </remarks>
public static class H264PpsParser
{

    // Section 7.4.2.1.1.1: "The value of delta_scale shall be in the range
    // of -128 to +127, inclusive."
    internal const Int32 kScalingDeltaMin = -128;

    internal const Int32 kScalingDeltaMax = 127;

    // Section 7.4.2.1.2: "The value of seq_parameter_set_id shall be in the
    // range of 0 to 31, inclusive."
    const UInt32 kSeqParameterSetIdMin = 0;

    const UInt32 kSeqParameterSetIdMax = 31;

    // Section 7.4.2.2: "The value of pic_parameter_set_id shall be in the
    // range of 0 to 255, inclusive."
    const UInt32 kPicParameterSetIdMin = 0;

    const UInt32 kPicParameterSetIdMax = 255;

    // Section A.2.1: "Picture parameter sets shall have num_slice_groups_minus1
    // in the range of 0 to 7, inclusive."
    const UInt32 kNumSliceGroupsMinus1Min = 0;

    const UInt32 kNumSliceGroupsMinus1Max = 7;

    // Section 7.4.2.2: "The value of slice_group_map_type shall be in the
    // range of 0 to 6, inclusive."
    const UInt32 kSliceGroupMapTypeMin = 0;

    const UInt32 kSliceGroupMapTypeMax = 6;

    // Section 7.4.2.2: "The value of num_ref_idx_l0_default_active_minus1
    // shall be in the range of 0 to 31, inclusive."
    const UInt32 kNumRefIdxL0DefaultActiveMinus1Min = 0;

    const UInt32 kNumRefIdxL0DefaultActiveMinus1Max = 31;

    // Section 7.4.2.2: "The value of num_ref_idx_l1_default_active_minus1
    // shall be in the range of 0 to 31, inclusive."
    const UInt32 kNumRefIdxL1DefaultActiveMinus1Min = 0;
    const UInt32 kNumRefIdxL1DefaultActiveMinus1Max = 31;


    /// <summary>
    /// Unpack RBSP and parse PPS state from the supplied buffer.
    /// </summary>
    public static PpsState ParsePps(ReadOnlySpan<byte> data, UInt32 chroma_format_idc)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new BitBuffer(unpacked_buffer.ToArray());
        return ParsePps(bit_buffer, chroma_format_idc);
    }

    private static PpsState? ParsePps(BitBuffer bit_buffer, UInt32 chroma_format_idc)
    {
        UInt32 bits_tmp;
        UInt32 golomb_tmp;

// H264 PPS NAL Unit (pic_parameter_set_rbsp()) parser.
// Section 7.3.2.2 ("Picture parameter set RBSP syntax") of the H.264
// standard for a complete description.
        var pps = new PpsState();

// input parameters
        pps.chroma_format_idc = chroma_format_idc;

        // pic_parameter_set_id  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out pps.pic_parameter_set_id))
        {
            return null;
        }

        if (pps.pic_parameter_set_id < kPicParameterSetIdMin ||
            pps.pic_parameter_set_id > kPicParameterSetIdMax)
        {
#if DEBUG
            Debug.WriteLine(
                $"invalid pic_parameter_set_id: {pps.pic_parameter_set_id} not in range [{kPicParameterSetIdMin}, {kPicParameterSetIdMax}]");
#endif
            return null;
        }

// seq_parameter_set_id  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out pps.seq_parameter_set_id))
        {
            return null;
        }

        if (pps.seq_parameter_set_id < kSeqParameterSetIdMin ||
            pps.seq_parameter_set_id > kSeqParameterSetIdMax)
        {
#if DEBUG
            Debug.WriteLine(
                $"invalid seq_parameter_set_id: {pps.seq_parameter_set_id} not in range [{kSeqParameterSetIdMin}, {kSeqParameterSetIdMax}]");
#endif
            return null;
        }

        // entropy_coding_mode_flag  u(1)
        if (!bit_buffer.ReadBits(1, out (pps.entropy_coding_mode_flag)))
        {
            return null;
        }

        // bottom_field_pic_order_in_frame_present_flag  u(1)
        if (!bit_buffer.ReadBits(1, out (pps.bottom_field_pic_order_in_frame_present_flag)))
        {
            return null;
        }

        // num_slice_groups_minus1  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out pps.num_slice_groups_minus1))
        {
            return null;
        }

        if (pps.num_slice_groups_minus1 < kNumSliceGroupsMinus1Min ||
            pps.num_slice_groups_minus1 > kNumSliceGroupsMinus1Max)
        {
#if DEBUG
            Debug.WriteLine(
                $"invalid num_slice_groups_minus1: {pps.num_slice_groups_minus1} not in range [{kNumSliceGroupsMinus1Min}, {kNumSliceGroupsMinus1Max}]");
#endif
            return null;
        }

        if (pps.num_slice_groups_minus1 > 0)
        {
            // slice_group_map_type  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out pps.slice_group_map_type))
            {
                return null;
            }

            if (pps.slice_group_map_type < kSliceGroupMapTypeMin ||
                pps.slice_group_map_type > kSliceGroupMapTypeMax)
            {
#if DEBUG
                Debug.WriteLine(
                    $"invalid slice_group_map_type: {pps.slice_group_map_type} not in range [{kSliceGroupMapTypeMin}, {kSliceGroupMapTypeMax}]");
#endif
                return null;
            }

            if (pps.slice_group_map_type == 0)
            {
                for (UInt32 iGroup = 0;
                     iGroup < pps.num_slice_groups_minus1;
                     iGroup++)
                {
                    // run_length_minus1[iGroup]  ue(v)
                    if (!bit_buffer.ReadExponentialGolomb(out golomb_tmp))
                    {
                        return null;
                    }

                    pps.run_length_minus1.Add(golomb_tmp);
                }

            }
            else if (pps.slice_group_map_type == 2)
            {
                for (UInt32 iGroup = 0;
                     iGroup < pps.num_slice_groups_minus1;
                     iGroup++)
                {
                    // top_left[iGroup]  ue(v)
                    if (!bit_buffer.ReadExponentialGolomb(out golomb_tmp))
                    {
                        return null;
                    }

                    pps.top_left.Add(golomb_tmp);

                    // bottom_right[iGroup]  ue(v)
                    if (!bit_buffer.ReadExponentialGolomb(out golomb_tmp))
                    {
                        return null;
                    }

                    pps.bottom_right.Add(golomb_tmp);
                }

            }
            else if ((pps.slice_group_map_type == 3) ||
                     (pps.slice_group_map_type == 4) ||
                     (pps.slice_group_map_type == 5))
            {
                // slice_group_change_direction_flag  u(1)
                if (!bit_buffer.ReadBits(1, out (pps.slice_group_change_direction_flag)))
                {
                    return null;
                }

                // slice_group_change_rate_minus1  ue(v)
                if (!bit_buffer.ReadExponentialGolomb(out pps.slice_group_change_rate_minus1))
                {
                    return null;
                }

            }
            else if (pps.slice_group_map_type == 6)
            {
                // pic_size_in_map_units_minus1  ue(v)
                if (!bit_buffer.ReadExponentialGolomb(out pps.pic_size_in_map_units_minus1))
                {
                    return null;
                }

                // slice_group_id  u(v)
                var slice_group_id_len = pps.getSliceGroupIdLen();
                if (!bit_buffer.ReadBits(slice_group_id_len, out bits_tmp))
                {
                    return null;
                }

                pps.slice_group_id.Add(bits_tmp);
            }
        }

// num_ref_idx_l0_default_active_minus1  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out pps.num_ref_idx_l0_default_active_minus1))
        {
            return null;
        }

        if (pps.num_ref_idx_l0_default_active_minus1 < kNumRefIdxL0DefaultActiveMinus1Min ||
            pps.num_ref_idx_l0_default_active_minus1 > kNumRefIdxL0DefaultActiveMinus1Max)
        {
#if DEBUG
            Debug.WriteLine(
                $"invalid num_ref_idx_l0_default_active_minus1: {pps.num_ref_idx_l0_default_active_minus1} not in range [{kNumRefIdxL0DefaultActiveMinus1Min}, {kNumRefIdxL0DefaultActiveMinus1Max}]");
#endif
            return null;
        }

// num_ref_idx_l1_default_active_minus1  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out pps.num_ref_idx_l1_default_active_minus1))
        {
            return null;
        }

        if (pps.num_ref_idx_l1_default_active_minus1 < kNumRefIdxL1DefaultActiveMinus1Min ||
            pps.num_ref_idx_l1_default_active_minus1 > kNumRefIdxL1DefaultActiveMinus1Max)
        {
#if DEBUG
            Debug.WriteLine(
                $"invalid num_ref_idx_l1_default_active_minus1: {pps.num_ref_idx_l1_default_active_minus1} not in range [{kNumRefIdxL1DefaultActiveMinus1Min}, {kNumRefIdxL1DefaultActiveMinus1Max}]");
#endif
            return null;
        }

// weighted_pred_flag  u(1)
        if (!bit_buffer.ReadBits(1, out (pps.weighted_pred_flag)))
        {
            return null;
        }

// weighted_bipred_idc  u(2)
        if (!bit_buffer.ReadBits(2, out (pps.weighted_bipred_idc)))
        {
            return null;
        }

// pic_init_qp_minus26  se(v)
        if (!bit_buffer.ReadSignedExponentialGolomb(out pps.pic_init_qp_minus26))
        {
            return null;
        }

// pic_init_qs_minus26  se(v)
        if (!bit_buffer.ReadSignedExponentialGolomb(out pps.pic_init_qs_minus26))
        {
            return null;
        }

// chroma_qp_index_offset  se(v)
        if (!bit_buffer.ReadSignedExponentialGolomb(out pps.chroma_qp_index_offset))
        {
            return null;
        }

// deblocking_filter_control_present_flag  u(1)
        if (!bit_buffer.ReadBits(1, out (pps.deblocking_filter_control_present_flag)))
        {
            return null;
        }

// constrained_intra_pred_flag  u(1)
        if (!bit_buffer.ReadBits(1, out (pps.constrained_intra_pred_flag)))
        {
            return null;
        }

// redundant_pic_cnt_present_flag  u(1)
        if (!bit_buffer.ReadBits(1, out (pps.redundant_pic_cnt_present_flag)))
        {
            return null;
        }

        if (H264Common.MoreRbspData(bit_buffer))
        {
            // transform_8x8_mode_flag  u(1)
            if (!bit_buffer.ReadBits(1, out (pps.transform_8x8_mode_flag)))
            {
                return null;
            }

            // pic_scaling_matrix_present_flag  u(1)
            if (!bit_buffer.ReadBits(1, out (pps.pic_scaling_matrix_present_flag)))
            {
                return null;
            }

            if (pps.pic_scaling_matrix_present_flag != 0)
            {
                var max_pic_scaling_list_present_flag =
                    6 + ((chroma_format_idc != 3) ? 2 : 6) * pps.transform_8x8_mode_flag;
                for (int i = 0; i < max_pic_scaling_list_present_flag; ++i)
                {
                    // pic_scaling_list_present_flag  u(1)
                    if (!bit_buffer.ReadBits(1, out bits_tmp))
                    {
                        return null;
                    }

                    pps.pic_scaling_list_present_flag.Add(bits_tmp);
                    if (pps.pic_scaling_list_present_flag[i] != 0)
                    {
                        // scaling_list()
                        if (i < 6)
                        {
                            pps.scaling_list(bit_buffer, i, pps.ScalingList4x4, 16, pps.UseDefaultScalingMatrix4x4Flag);
                        }
                        else
                        {
                            pps.scaling_list(bit_buffer, i - 6, pps.ScalingList8x8, 64,
                                pps.UseDefaultScalingMatrix4x4Flag);
                        }
                    }
                }
            }

            // second_chroma_qp_index_offset  se(v)
            if (!bit_buffer.ReadSignedExponentialGolomb(out pps.second_chroma_qp_index_offset))
            {
                return null;
            }
        }

        H264Common.rbsp_trailing_bits(bit_buffer);

        return pps;
    }
}
