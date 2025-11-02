using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// All the members on this sequence parameter set structure match the sequence parameter set syntax as specified by the H264 specification.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SupportedOSPlatform("linux")]
public struct V4L2CtrlH264Sps
{
    public byte profile_idc;
    public V4L2H264SpsConstraintSetFlag constraint_set_flags;
    public byte level_idc;
    public byte seq_parameter_set_id;
    public byte chroma_format_idc;
    public byte bit_depth_luma_minus8;
    public byte bit_depth_chroma_minus8;
    public byte log2_max_frame_num_minus4;
    public byte pic_order_cnt_type;
    public byte log2_max_pic_order_cnt_lsb_minus4;
    public byte max_num_ref_frames;
    public byte num_ref_frames_in_pic_order_cnt_cycle;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 255)]
    public int[] offset_for_ref_frame;

    public int offset_for_non_ref_pic;
    public int offset_for_top_to_bottom_field;
    public ushort pic_width_in_mbs_minus1;
    public ushort pic_height_in_map_units_minus1;

    /// <summary>
    /// see V4L2_H264_SPS_FLAG_{}
    /// </summary>
    public V4L2H264SpsFlag flags;
}