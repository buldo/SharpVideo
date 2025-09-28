namespace SharpVideo.H264;

/// <summary>
/// The parsed state of the slice. Only some select values are stored. Add more as they are actually needed.
/// </summary>
public class SliceHeaderState
{

    // input parameters
    public uint32_t nal_ref_idc = 0;
    public uint32_t nal_unit_type = 0;
    public uint32_t separate_colour_plane_flag = 0;
    public uint32_t log2_max_frame_num_minus4 = 0;
    public uint32_t frame_mbs_only_flag = 0;
    public uint32_t pic_order_cnt_type = 0;
    public uint32_t bottom_field_pic_order_in_frame_present_flag = 0;
    public uint32_t delta_pic_order_always_zero_flag = 0;
    public uint32_t redundant_pic_cnt_present_flag = 0;
    public uint32_t weighted_pred_flag = 0;
    public uint32_t weighted_bipred_idc = 0;
    public uint32_t entropy_coding_mode_flag = 0;
    public uint32_t deblocking_filter_control_present_flag = 0;
    public uint32_t num_slice_groups_minus1 = 0;
    public uint32_t slice_group_map_type = 0;
    public uint32_t pic_width_in_mbs_minus1 = 0;
    public uint32_t pic_height_in_map_units_minus1 = 0;
    public uint32_t slice_group_change_rate_minus1 = 0;

    // contents
    public uint32_t first_mb_in_slice = 0;
    public uint32_t slice_type = 0;
    public uint32_t pic_parameter_set_id = 0;
    public uint32_t colour_plane_id = 0;
    public uint32_t frame_num = 0;
    public uint32_t field_pic_flag = 0;
    public uint32_t bottom_field_flag = 0;
    public uint32_t idr_pic_id = 0;
    public uint32_t pic_order_cnt_lsb = 0;
    public int32_t delta_pic_order_cnt_bottom = 0;
    public List<int32_t> delta_pic_order_cnt;
    public uint32_t redundant_pic_cnt = 0;
    public uint32_t direct_spatial_mv_pred_flag = 0;
    public uint32_t num_ref_idx_active_override_flag = 0;
    public uint32_t num_ref_idx_l0_active_minus1 = 0;
    public uint32_t num_ref_idx_l1_active_minus1 = 0;
    public RefPicListModificationState ref_pic_list_modification;
    public PredWeightTableState pred_weight_table;
    public DecRefPicMarkingState dec_ref_pic_marking;
    public uint32_t cabac_init_idc = 0;
    public uint32_t sp_for_switch_flag = 0;
    public int32_t slice_qp_delta = 0;
    public int32_t slice_qs_delta = 0;
    public uint32_t disable_deblocking_filter_idc = 0;
    public int32_t slice_alpha_c0_offset_div2 = 0;
    public int32_t slice_beta_offset_div2 = 0;
    public uint32_t slice_group_change_cycle = 0;

    // derived values
    static uint32_t getFrameNumLen(uint32_t log2_max_frame_num_minus4)
    {
        // Rec. ITU-T H.264 (2012) Page 62, Section 7.4.3
        // frame_num is used as an identifier for pictures and shall be
        // represented by log2_max_frame_num_minus4 + 4 bits in the bitstream.
        return log2_max_frame_num_minus4 + 4;
    }
    static uint32_t getPicOrderCntLsbLen(uint32_t log2_max_pic_order_cnt_lsb_minus4)
    {
        // Rec. ITU-T H.264 (2012) Page 64, Section 7.4.3
        // The size of the pic_order_cnt_lsb syntax element is
        // log2_max_pic_order_cnt_lsb_minus4 + 4 bits.
        return log2_max_pic_order_cnt_lsb_minus4 + 4;
    }
    static uint32_t getSliceGroupChangeCycleLen(uint32_t pic_width_in_mbs_minus1, uint32_t pic_height_in_map_units_minus1, uint32_t slice_group_change_rate_minus1)
    {
        // Rec. ITU-T H.264 (2012) Page 67, Section 7.4.3
        // The value of slice_group_change_cycle is represented in the bitstream
        // by the following number of bits
        // Ceil(Log2(PicSizeInMapUnits � SliceGroupChangeRate + 1)) (7-21)
        uint32_t PicSizeInMapUnits = getPicSizeInMapUnits(
            pic_width_in_mbs_minus1, pic_height_in_map_units_minus1);
        uint32_t SliceGroupChangeRate =
            getSliceGroupChangeRate(slice_group_change_rate_minus1);
        return (uint32_t)Math.Ceiling(Math.Log2(((1.0 * PicSizeInMapUnits) / SliceGroupChangeRate) + 1));
    }

    static uint32_t getPicWidthInMbs(uint32_t pic_width_in_mbs_minus1)
    {
        return pic_width_in_mbs_minus1 + 1;
    }
    static uint32_t getPicHeightInMapUnits(uint32_t pic_height_in_map_units_minus1)
    {
        return pic_height_in_map_units_minus1 + 1;
    }
    static uint32_t getPicSizeInMapUnits(uint32_t pic_width_in_mbs_minus1, uint32_t pic_height_in_map_units_minus1)
    {
        uint32_t PicWidthInMbs = getPicWidthInMbs(pic_width_in_mbs_minus1);
        uint32_t PicHeightInMapUnits =
            getPicHeightInMapUnits(pic_height_in_map_units_minus1);
        return PicWidthInMbs * PicHeightInMapUnits;
    }
    static uint32_t getSliceGroupChangeRate(uint32_t slice_group_change_rate_minus1)
    {
        return slice_group_change_rate_minus1 + 1;
    }
}