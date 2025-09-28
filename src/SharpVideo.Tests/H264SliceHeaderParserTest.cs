using System;

using SharpVideo.H264;

namespace SharpVideo.Tests;

public class H264SliceHeaderParserTest
{
    [Fact]
    public void TestSampleSliceIDR601()
    {
        // fuzzer::conv: data
        byte[] buffer =
        {
            0x88, 0x82, 0x06, 0x78, 0x8c, 0x50, 0x00, 0x1c,
            0xab, 0x8e, 0x00, 0x02, 0xfb, 0x31, 0xc0, 0x00,
            0x5f, 0x66, 0xfb, 0xef, 0xbe
        };

        // fuzzer::conv: begin
        // get some mock state
        H264BitstreamParserState bitstream_parser_state = new();
        var sps = new SpsState();
        sps.sps_data = new SpsDataState();
        sps.sps_data.log2_max_frame_num_minus4 = 1;
        sps.sps_data.frame_mbs_only_flag = 1;
        sps.sps_data.pic_order_cnt_type = 2;
        sps.sps_data.delta_pic_order_always_zero_flag = 0;
        sps.sps_data.pic_width_in_mbs_minus1 = 0;
        sps.sps_data.pic_height_in_map_units_minus1 = 0;
        bitstream_parser_state.sps[0] = sps;
        var pps = new PpsState();
        pps.bottom_field_pic_order_in_frame_present_flag = 0;
        pps.redundant_pic_cnt_present_flag = 0;
        pps.weighted_pred_flag = 0;
        pps.weighted_bipred_idc = 0;
        pps.entropy_coding_mode_flag = 0;
        pps.deblocking_filter_control_present_flag = 1;
        pps.num_slice_groups_minus1 = 0;
        pps.slice_group_map_type = 0;
        pps.slice_group_change_rate_minus1 = 0;
        ;
        bitstream_parser_state.pps[0] = pps;

        UInt32 nal_ref_idc = 3;
        UInt32 nal_unit_type = (uint)H264NaluType.CodedSliceIdr;
        var slice_header = H264SliceHeaderParser.ParseSliceHeader(buffer, nal_ref_idc, nal_unit_type, bitstream_parser_state);
        // fuzzer::conv: end

        Assert.NotNull(slice_header);

        Assert.Equal(0u, slice_header.first_mb_in_slice);
        Assert.Equal(7u, slice_header.slice_type);
        Assert.Equal(0u, slice_header.pic_parameter_set_id);
        Assert.Equal(0u, slice_header.frame_num);
        Assert.Equal(0u, slice_header.field_pic_flag);
        Assert.Equal(0u, slice_header.bottom_field_flag);
        Assert.Equal(0u, slice_header.idr_pic_id);
        Assert.Equal(0u, slice_header.pic_order_cnt_lsb);
        Assert.Equal(0, slice_header.delta_pic_order_cnt_bottom);
        Assert.Equal(0, slice_header.delta_pic_order_cnt.Count);
        Assert.Equal(0u, slice_header.redundant_pic_cnt);
        Assert.Equal(0u, slice_header.direct_spatial_mv_pred_flag);
        Assert.Equal(0u, slice_header.num_ref_idx_active_override_flag);
        Assert.Equal(0u, slice_header.num_ref_idx_l0_active_minus1);
        Assert.Equal(0u, slice_header.num_ref_idx_l1_active_minus1);

        Assert.NotNull(slice_header.ref_pic_list_modification);
        Assert.Equal(0u, slice_header.ref_pic_list_modification.ref_pic_list_modification_flag_l0);
        Assert.Equal(0u, slice_header.ref_pic_list_modification.ref_pic_list_modification_flag_l1);
        Assert.Equal(0, slice_header.ref_pic_list_modification.modification_of_pic_nums_idc.Count);
        Assert.Equal(0, slice_header.ref_pic_list_modification.abs_diff_pic_num_minus1.Count);
        Assert.Equal(0, slice_header.ref_pic_list_modification.long_term_pic_num.Count);

        Assert.Null(slice_header.pred_weight_table);

        Assert.NotNull(slice_header.dec_ref_pic_marking);
        Assert.Equal(0u, slice_header.dec_ref_pic_marking.no_output_of_prior_pics_flag);
        Assert.Equal(0u, slice_header.dec_ref_pic_marking.long_term_reference_flag);
        Assert.Equal(0u, slice_header.dec_ref_pic_marking.adaptive_ref_pic_marking_mode_flag);
        Assert.Equal(0, slice_header.dec_ref_pic_marking.memory_management_control_operation.Count);
        Assert.Equal(0, slice_header.dec_ref_pic_marking.difference_of_pic_nums_minus1.Count);
        Assert.Equal(0, slice_header.dec_ref_pic_marking.long_term_pic_num.Count);
        Assert.Equal(0, slice_header.dec_ref_pic_marking.long_term_frame_idx.Count);
        Assert.Equal(0, slice_header.dec_ref_pic_marking.max_long_term_frame_idx_plus1.Count);

        Assert.Equal(0u, slice_header.cabac_init_idc);
        Assert.Equal(0u, slice_header.sp_for_switch_flag);
        Assert.Equal(-12, slice_header.slice_qp_delta);
        Assert.Equal(0, slice_header.slice_qs_delta);
        Assert.Equal(0u, slice_header.disable_deblocking_filter_idc);
        Assert.Equal(0, slice_header.slice_alpha_c0_offset_div2);
        Assert.Equal(0, slice_header.slice_beta_offset_div2);
        Assert.Equal(0u, slice_header.slice_group_change_cycle);
    }

    [Fact]
    public void TestSampleSliceNonIDR601()
    {
        // fuzzer::conv: data
        byte[] buffer =
        {
            0x9a, 0x1c, 0x0c, 0xf0, 0x09, 0x6c
        };

        // fuzzer::conv: begin
        // get some mock state
        H264BitstreamParserState bitstream_parser_state =new();
        var sps = new SpsState();
        sps.sps_data = new SpsDataState();
        sps.sps_data.log2_max_frame_num_minus4 = 1;
        sps.sps_data.frame_mbs_only_flag = 1;
        sps.sps_data.pic_order_cnt_type = 2;
        sps.sps_data.delta_pic_order_always_zero_flag = 0;
        sps.sps_data.pic_width_in_mbs_minus1 = 0;
        sps.sps_data.pic_height_in_map_units_minus1 = 0;
        bitstream_parser_state.sps[0] = sps;
        var pps = new PpsState();
        pps.bottom_field_pic_order_in_frame_present_flag = 0;
        pps.redundant_pic_cnt_present_flag = 0;
        pps.weighted_pred_flag = 0;
        pps.weighted_bipred_idc = 0;
        pps.entropy_coding_mode_flag = 0;
        pps.deblocking_filter_control_present_flag = 1;
        pps.num_slice_groups_minus1 = 0;
        pps.slice_group_map_type = 0;
        pps.slice_group_change_rate_minus1 = 0;
        ;
        bitstream_parser_state.pps[0] = pps;

        UInt32 nal_ref_idc = 2;
        UInt32 nal_unit_type = (uint)H264NaluType.CodedSliceNonIdr;
        var slice_header = H264SliceHeaderParser.ParseSliceHeader(buffer, nal_ref_idc, nal_unit_type, bitstream_parser_state);
        // fuzzer::conv: end

        Assert.NotNull(slice_header);

        Assert.Equal(0u, slice_header.first_mb_in_slice);
        Assert.Equal(5u, slice_header.slice_type);
        Assert.Equal(0u, slice_header.pic_parameter_set_id);
        Assert.Equal(1u, slice_header.frame_num);
        Assert.Equal(0u, slice_header.field_pic_flag);
        Assert.Equal(0u, slice_header.bottom_field_flag);
        Assert.Equal(0u, slice_header.idr_pic_id);
        Assert.Equal(0u, slice_header.pic_order_cnt_lsb);
        Assert.Equal(0, slice_header.delta_pic_order_cnt_bottom);
        Assert.Equal(0, slice_header.delta_pic_order_cnt.Count);
        Assert.Equal(0u, slice_header.redundant_pic_cnt);
        Assert.Equal(0u, slice_header.direct_spatial_mv_pred_flag);
        Assert.Equal(1u, slice_header.num_ref_idx_active_override_flag);
        Assert.Equal(0u, slice_header.num_ref_idx_l0_active_minus1);
        Assert.Equal(0u, slice_header.num_ref_idx_l1_active_minus1);

        Assert.NotNull(slice_header.ref_pic_list_modification);
        Assert.Equal(0u, slice_header.ref_pic_list_modification.ref_pic_list_modification_flag_l0);
        Assert.Equal(0u, slice_header.ref_pic_list_modification.ref_pic_list_modification_flag_l1);
        Assert.Equal(0, slice_header.ref_pic_list_modification.modification_of_pic_nums_idc.Count);
        Assert.Equal(0, slice_header.ref_pic_list_modification.abs_diff_pic_num_minus1.Count);
        Assert.Equal(0, slice_header.ref_pic_list_modification.long_term_pic_num.Count);

        Assert.Null(slice_header.pred_weight_table);

        Assert.NotNull(slice_header.dec_ref_pic_marking);
        Assert.Equal(0u, slice_header.dec_ref_pic_marking.no_output_of_prior_pics_flag);
        Assert.Equal(0u, slice_header.dec_ref_pic_marking.long_term_reference_flag);
        Assert.Equal(0u, slice_header.dec_ref_pic_marking.adaptive_ref_pic_marking_mode_flag);
        Assert.Equal(0, slice_header.dec_ref_pic_marking.memory_management_control_operation.Count);
        Assert.Equal(0, slice_header.dec_ref_pic_marking.difference_of_pic_nums_minus1.Count);
        Assert.Equal(0, slice_header.dec_ref_pic_marking.long_term_pic_num.Count);
        Assert.Equal(0, slice_header.dec_ref_pic_marking.long_term_frame_idx.Count);
        Assert.Equal(0, slice_header.dec_ref_pic_marking.max_long_term_frame_idx_plus1.Count);

        Assert.Equal(0u, slice_header.cabac_init_idc);
        Assert.Equal(0u, slice_header.sp_for_switch_flag);
        Assert.Equal(-12, slice_header.slice_qp_delta);
        Assert.Equal(0, slice_header.slice_qs_delta);
        Assert.Equal(0u, slice_header.disable_deblocking_filter_idc);
        Assert.Equal(0, slice_header.slice_alpha_c0_offset_div2);
        Assert.Equal(0, slice_header.slice_beta_offset_div2);
        Assert.Equal(0u, slice_header.slice_group_change_cycle);
    }
}