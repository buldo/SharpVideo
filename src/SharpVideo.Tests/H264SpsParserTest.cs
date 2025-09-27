using SharpVideo.H264;

namespace SharpVideo.Tests;

public class H264SpsParserTest
{
    [Fact]
    public void TestSampleSPS601()
    {
        // SPS (601.264)
        // fuzzer::conv: data
        byte[] buffer =
        {
            0x42, 0xc0, 0x16, 0xa6, 0x11, 0x05, 0x07, 0xe9,
            0xb2, 0x00, 0x00, 0x03, 0x00, 0x02, 0x00, 0x00,
            0x03, 0x00, 0x64, 0x1e, 0x2c, 0x5c, 0x23, 0x00
        };
        // fuzzer::conv: begin
        var sps = H264SpsParser.ParseSps(buffer);
        // fuzzer::conv: end

        Assert.NotNull(sps);

        // seq_parameter_set_data()
        var sps_data = sps.sps_data;
        Assert.NotNull(sps_data);
        Assert.Equal(66u, sps_data.profile_idc);
        Assert.Equal(1u, sps_data.constraint_set0_flag);
        Assert.Equal(1u, sps_data.constraint_set1_flag);
        Assert.Equal(0u, sps_data.constraint_set2_flag);
        Assert.Equal(0u, sps_data.constraint_set3_flag);
        Assert.Equal(0u, sps_data.constraint_set4_flag);
        Assert.Equal(0u, sps_data.constraint_set5_flag);
        Assert.Equal(0u, sps_data.reserved_zero_2bits);
        Assert.Equal(22u, sps_data.level_idc);
        Assert.Equal(0u, sps_data.seq_parameter_set_id);
        Assert.Equal(1u, sps_data.log2_max_frame_num_minus4);
        Assert.Equal(2u, sps_data.pic_order_cnt_type);
        Assert.Equal(16u, sps_data.max_num_ref_frames);
        Assert.Equal(0u, sps_data.gaps_in_frame_num_value_allowed_flag);
        Assert.Equal(19u, sps_data.pic_width_in_mbs_minus1);
        Assert.Equal(14u, sps_data.pic_height_in_map_units_minus1);
        Assert.Equal(1u, sps_data.frame_mbs_only_flag);
        Assert.Equal(1u, sps_data.direct_8x8_inference_flag);
        Assert.Equal(0u, sps_data.frame_cropping_flag);
        Assert.Equal(1u, sps_data.vui_parameters_present_flag);

        // vui_parameters()
        var vui_parameters = sps_data.vui_parameters;
        Assert.NotNull(vui_parameters );
        Assert.Equal(0u, vui_parameters.aspect_ratio_info_present_flag);
        Assert.Equal(0u, vui_parameters.overscan_info_present_flag);
        Assert.Equal(1u, vui_parameters.video_signal_type_present_flag);
        Assert.Equal(5u, vui_parameters.video_format);
        Assert.Equal(1u, vui_parameters.video_full_range_flag);
        Assert.Equal(0u, vui_parameters.colour_description_present_flag);
        Assert.Equal(0u, vui_parameters.chroma_loc_info_present_flag);
        Assert.Equal(1u, vui_parameters.timing_info_present_flag);
        Assert.Equal(1u, vui_parameters.num_units_in_tick);
        Assert.Equal(50u, vui_parameters.time_scale);
        Assert.Equal(0u, vui_parameters.fixed_frame_rate_flag);
        Assert.Equal(0u, vui_parameters.nal_hrd_parameters_present_flag);
        Assert.Equal(0u, vui_parameters.vcl_hrd_parameters_present_flag);
        Assert.Equal(0u, vui_parameters.pic_struct_present_flag);
        Assert.Equal(1u, vui_parameters.bitstream_restriction_flag);
        Assert.Equal(1u, vui_parameters.motion_vectors_over_pic_boundaries_flag);
        Assert.Equal(0u, vui_parameters.max_bytes_per_pic_denom);
        Assert.Equal(0u, vui_parameters.max_bits_per_mb_denom);
        Assert.Equal(10u, vui_parameters.log2_max_mv_length_horizontal);
        Assert.Equal(10u, vui_parameters.log2_max_mv_length_vertical);
        Assert.Equal(0u, vui_parameters.max_num_reorder_frames);
        Assert.Equal(16u, vui_parameters.max_dec_frame_buffering);

    }

    [Fact]
    public void TestSampleSPS2012()
    {
        // SPS (2012 source)
        // fuzzer::conv: data
        byte[] buffer =
        {
            0x64, 0x00, 0x33, 0xac, 0x72, 0x84, 0x40, 0x78,
            0x02, 0x27, 0xe5, 0xc0, 0x44, 0x00, 0x00, 0x03,
            0x00, 0x04, 0x00, 0x00, 0x03, 0x00, 0xf0, 0x3c,
            0x60, 0xc6, 0x11, 0x80
        };
        var sps = H264SpsParser.ParseSps(buffer);

        Assert.NotNull(sps);

        // seq_parameter_set_data()
        var sps_data = sps.sps_data;
        Assert.NotNull(sps_data);
        Assert.Equal(100u, sps_data.profile_idc);
        Assert.Equal(0u, sps_data.constraint_set0_flag);
        Assert.Equal(0u, sps_data.constraint_set1_flag);
        Assert.Equal(0u, sps_data.constraint_set2_flag);
        Assert.Equal(0u, sps_data.constraint_set3_flag);
        Assert.Equal(0u, sps_data.constraint_set4_flag);
        Assert.Equal(0u, sps_data.constraint_set5_flag);
        Assert.Equal(0u, sps_data.reserved_zero_2bits);
        Assert.Equal(51u, sps_data.level_idc);
        Assert.Equal(0u, sps_data.seq_parameter_set_id);
        Assert.Equal(1u, sps_data.chroma_format_idc);

        Assert.Equal(0u, sps_data.bit_depth_luma_minus8);
        Assert.Equal(0u, sps_data.bit_depth_chroma_minus8);
        Assert.Equal(0u, sps_data.qpprime_y_zero_transform_bypass_flag);
        Assert.Equal(0u, sps_data.seq_scaling_matrix_present_flag);
        Assert.Equal(2u, sps_data.log2_max_frame_num_minus4);
        Assert.Equal(0u, sps_data.pic_order_cnt_type);
        Assert.Equal(4u, sps_data.log2_max_pic_order_cnt_lsb_minus4);
        Assert.Equal(16u, sps_data.max_num_ref_frames);
        Assert.Equal(0u, sps_data.gaps_in_frame_num_value_allowed_flag);
        Assert.Equal(119u, sps_data.pic_width_in_mbs_minus1);
        Assert.Equal(67u, sps_data.pic_height_in_map_units_minus1);
        Assert.Equal(1u, sps_data.frame_mbs_only_flag);
        Assert.Equal(1u, sps_data.direct_8x8_inference_flag);
        Assert.Equal(1u, sps_data.frame_cropping_flag);
        Assert.Equal(0u, sps_data.frame_crop_left_offset);
        Assert.Equal(0u, sps_data.frame_crop_right_offset);
        Assert.Equal(0u, sps_data.frame_crop_top_offset);
        Assert.Equal(4u, sps_data.frame_crop_bottom_offset);
        Assert.Equal(1u, sps_data.vui_parameters_present_flag);

        // vui_parameters()
        var vui_parameters = sps_data.vui_parameters;
        Assert.NotNull(vui_parameters);
        Assert.Equal(1u, vui_parameters.aspect_ratio_info_present_flag);
        Assert.Equal(1u, vui_parameters.aspect_ratio_idc);
        Assert.Equal(0u, vui_parameters.overscan_info_present_flag);
        Assert.Equal(0u, vui_parameters.video_signal_type_present_flag);
        Assert.Equal(0u, vui_parameters.chroma_loc_info_present_flag);
        Assert.Equal(1u, vui_parameters.timing_info_present_flag);
        Assert.Equal(1u, vui_parameters.num_units_in_tick);
        Assert.Equal(60u, vui_parameters.time_scale);
        Assert.Equal(0u, vui_parameters.fixed_frame_rate_flag);
        Assert.Equal(0u, vui_parameters.nal_hrd_parameters_present_flag);
        Assert.Equal(0u, vui_parameters.vcl_hrd_parameters_present_flag);
        Assert.Equal(0u, vui_parameters.pic_struct_present_flag);
        Assert.Equal(1u, vui_parameters.bitstream_restriction_flag);
        Assert.Equal(1u, vui_parameters.motion_vectors_over_pic_boundaries_flag);
        Assert.Equal(0u, vui_parameters.max_bytes_per_pic_denom);
        Assert.Equal(0u, vui_parameters.max_bits_per_mb_denom);
        Assert.Equal(11u, vui_parameters.log2_max_mv_length_horizontal);
        Assert.Equal(11u, vui_parameters.log2_max_mv_length_vertical);
        Assert.Equal(2u, vui_parameters.max_num_reorder_frames);
        Assert.Equal(16u, vui_parameters.max_dec_frame_buffering);
    }
}