using System;
using SharpVideo.H264;

namespace SharpVideo.Tests;

public class H264PpsParserTest
{
    [Fact]
    public void TestSamplePPS601()
    {
        // PPS example (601.264)
        // fuzzer::conv: data
        byte[] buffer = { 0xc8, 0x42, 0x02, 0x32, 0xc8 };
        // fuzzer::conv: begin
        UInt32 chroma_format_idc = 1;
        var pps = H264PpsParser.ParsePps(buffer, chroma_format_idc);
        // fuzzer::conv: end

        Assert.NotNull(pps);

        Assert.Equal(0u, pps.pic_parameter_set_id);
        Assert.Equal(0u, pps.seq_parameter_set_id);
        Assert.Equal(0u, pps.entropy_coding_mode_flag);
        Assert.Equal(0u, pps.bottom_field_pic_order_in_frame_present_flag);
        Assert.Equal(0u, pps.num_slice_groups_minus1);
        Assert.Equal(15u, pps.num_ref_idx_l0_default_active_minus1);
        Assert.Equal(0u, pps.num_ref_idx_l1_default_active_minus1);
        Assert.Equal(0u, pps.weighted_pred_flag);
        Assert.Equal(0u, pps.weighted_bipred_idc);
        Assert.Equal(-8, pps.pic_init_qp_minus26);
        Assert.Equal(0, pps.pic_init_qs_minus26);
        Assert.Equal(-2, pps.chroma_qp_index_offset);
        Assert.Equal(1u, pps.deblocking_filter_control_present_flag);
        Assert.Equal(0u, pps.constrained_intra_pred_flag);
        Assert.Equal(0u, pps.redundant_pic_cnt_present_flag);
    }

    [Fact]
    public void TestSamplePPS2012()
    {
        // PPS example (2012 source)
        // fuzzer::conv: data
        byte[] buffer = { 0xe8, 0x43, 0x82, 0x92, 0xc8, 0xb0 };

        // fuzzer::conv: begin
        UInt32 chroma_format_idc = 1;
        var pps = H264PpsParser.ParsePps(buffer, chroma_format_idc);
        // fuzzer::conv: end

        Assert.NotNull(pps);

        Assert.Equal(0u, pps.pic_parameter_set_id);
        Assert.Equal(0u, pps.seq_parameter_set_id);
        Assert.Equal(1u, pps.entropy_coding_mode_flag);
        Assert.Equal(0u, pps.bottom_field_pic_order_in_frame_present_flag);
        Assert.Equal(0u, pps.num_slice_groups_minus1);
        Assert.Equal(15u, pps.num_ref_idx_l0_default_active_minus1);
        Assert.Equal(0u, pps.num_ref_idx_l1_default_active_minus1);
        Assert.Equal(1u, pps.weighted_pred_flag);
        Assert.Equal(2u, pps.weighted_bipred_idc);
        Assert.Equal(10, pps.pic_init_qp_minus26);
        Assert.Equal(0, pps.pic_init_qs_minus26);
        Assert.Equal(-2, pps.chroma_qp_index_offset);
        Assert.Equal(1u, pps.deblocking_filter_control_present_flag);
        Assert.Equal(0u, pps.constrained_intra_pred_flag);
        Assert.Equal(0u, pps.redundant_pic_cnt_present_flag);
        Assert.Equal(1u, pps.transform_8x8_mode_flag);
        Assert.Equal(0u, pps.pic_scaling_matrix_present_flag);
        Assert.Equal(-2, pps.second_chroma_qp_index_offset);
    }
}