namespace SharpVideo.H264;

/// <summary>
/// The parsed state of the PPS. Only some select values are stored.
/// Add more as they are actually needed.
/// </summary>
public class PpsState
{
    // input parameters
    public UInt32 chroma_format_idc = 0;

    // contents
    public UInt32 pic_parameter_set_id = 0;
    public UInt32 seq_parameter_set_id = 0;
    public UInt32 entropy_coding_mode_flag = 0;
    public UInt32 bottom_field_pic_order_in_frame_present_flag = 0;
    public UInt32 num_slice_groups_minus1 = 0;
    public UInt32 slice_group_map_type = 0;
    public List<UInt32> run_length_minus1;
    public List<UInt32> top_left;
    public List<UInt32> bottom_right;
    public UInt32 slice_group_change_direction_flag = 0;
    public UInt32 slice_group_change_rate_minus1 = 0;
    public UInt32 pic_size_in_map_units_minus1 = 0;
    public List<UInt32> slice_group_id;
    public UInt32 num_ref_idx_l0_default_active_minus1 = 0;
    public UInt32 num_ref_idx_l1_default_active_minus1 = 0;
    public UInt32 weighted_pred_flag = 0;
    public UInt32 weighted_bipred_idc = 0;
    public Int32 pic_init_qp_minus26 = 0;
    public Int32 pic_init_qs_minus26 = 0;
    public Int32 chroma_qp_index_offset = 0;
    public UInt32 deblocking_filter_control_present_flag = 0;
    public UInt32 constrained_intra_pred_flag = 0;
    public UInt32 redundant_pic_cnt_present_flag = 0;
    public UInt32 transform_8x8_mode_flag = 0;
    public UInt32 pic_scaling_matrix_present_flag = 0;
    public List<UInt32> pic_scaling_list_present_flag;
    // scaling_list()
    public List<UInt32> ScalingList4x4;
    public List<UInt32> UseDefaultScalingMatrix4x4Flag;
    public List<UInt32> ScalingList8x8;
    public List<UInt32> UseDefaultScalingMatrix8x8Flag;
    public Int32 delta_scale = 0;
    public Int32 second_chroma_qp_index_offset = 0;

    public int getSliceGroupIdLen()
    {
        // Rec. ITU-T H.264 (2012) Page 70, Section 7.4.2.2
        // slice_group_id[i] identifies a slice group of the i-th slice group
        // map unit in raster scan order. The size of the slice_group_id[i]
        // syntax element is `Ceil(Log2(num_slice_groups_minus1 + 1))` bits.
        // The value of slice_group_id[i] shall be in the range of 0 to
        // num_slice_groups_minus1, inclusive.
        return (int)Math.Ceiling(Math.Log2(1.0 * num_slice_groups_minus1 + 1));
    }

    // Section 7.3.2.1.1.1
    public bool scaling_list(
        BitBuffer bitBuffer,
        int i,
        List<uint> scalingList,
        uint sizeOfScalingList,
        List<uint> useDefaultScalingMatrixFlag)
    {
        uint lastScale = 8;
        uint nextScale = 8;
        for (uint j = 0; j < sizeOfScalingList; j++)
        {
            if (nextScale != 0)
            {
                // delta_scale  se(v)
                int delta_scale;
                if (!bitBuffer.ReadSignedExponentialGolomb(out delta_scale))
                {
                    return false;
                }
                if (delta_scale < H264PpsParser.kScalingDeltaMin ||
                    delta_scale > H264PpsParser.kScalingDeltaMax)
                {
                    // Console.Error.WriteLine(
                    //     $"invalid delta_scale: {delta_scale} not in range [{H264PpsParser.kScalingDeltaMin}, {H264PpsParser.kScalingDeltaMax}]");
                    return false;
                }
                nextScale = (uint)((lastScale + delta_scale + 256) % 256);

                // make sure list has ith element
                while (useDefaultScalingMatrixFlag.Count <= i)
                {
                    useDefaultScalingMatrixFlag.Add(0);
                }
                useDefaultScalingMatrixFlag[(int)i] = (j == 0 && nextScale == 0) ? 1u : 0u;
            }
            // make sure list has jth element
            while (scalingList.Count <= j)
            {
                scalingList.Add(0);
            }
            scalingList[(int)j] = (nextScale == 0) ? lastScale : nextScale;
            lastScale = scalingList[(int)j];
        }
        return true;
    }
};