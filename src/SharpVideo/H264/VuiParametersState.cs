namespace SharpVideo.H264;

public class VuiParametersState
{
#if DEBUG
    //void fdump(FILE* outfp, int indent_level);
#endif

    public UInt32 aspect_ratio_info_present_flag = 0;
    public UInt32 aspect_ratio_idc = 0;
    public UInt32 sar_width = 0;
    public UInt32 sar_height = 0;
    public UInt32 overscan_info_present_flag = 0;
    public UInt32 overscan_appropriate_flag = 0;
    public UInt32 video_signal_type_present_flag = 0;
    public UInt32 video_format = 0;
    public UInt32 video_full_range_flag = 0;
    public UInt32 colour_description_present_flag = 0;
    public UInt32 colour_primaries = 0;
    public UInt32 transfer_characteristics = 0;
    public UInt32 matrix_coefficients = 0;
    public UInt32 chroma_loc_info_present_flag = 0;
    public UInt32 chroma_sample_loc_type_top_field = 0;
    public UInt32 chroma_sample_loc_type_bottom_field = 0;
    public UInt32 timing_info_present_flag = 0;
    public UInt32 num_units_in_tick = 0;
    public UInt32 time_scale = 0;
    public UInt32 fixed_frame_rate_flag = 0;
    public UInt32 nal_hrd_parameters_present_flag = 0;
    public HrdParametersState nal_hrd_parameters;
    public UInt32 vcl_hrd_parameters_present_flag = 0;
    public HrdParametersState vcl_hrd_parameters;
    public UInt32 low_delay_hrd_flag = 0;
    public UInt32 pic_struct_present_flag = 0;
    public UInt32 bitstream_restriction_flag = 0;
    public UInt32 motion_vectors_over_pic_boundaries_flag = 0;
    public UInt32 max_bytes_per_pic_denom = 0;
    public UInt32 max_bits_per_mb_denom = 0;
    public UInt32 log2_max_mv_length_horizontal = 0;
    public UInt32 log2_max_mv_length_vertical = 0;
    public UInt32 max_num_reorder_frames = 0;
    public UInt32 max_dec_frame_buffering = 0;

    // derived values
    public float getFramerate()
    {
        // Equation D-2
        float framerate = (float)time_scale / ((float)2.0 * (float)num_units_in_tick);
        return framerate;
    }
}