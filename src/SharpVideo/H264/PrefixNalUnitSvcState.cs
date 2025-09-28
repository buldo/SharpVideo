namespace SharpVideo.H264;

public class PrefixNalUnitSvcState
{
    // input parameters
    public uint32_t nal_ref_idc = 0;
    public uint32_t use_ref_base_pic_flag = 0;
    public uint32_t idr_flag = 0;

    // contents
    public uint32_t store_ref_base_pic_flag = 0;

    // TODO(chemag): dec_ref_base_pic_marking()
    public uint32_t additional_prefix_nal_unit_extension_flag = 0;
    public uint32_t additional_prefix_nal_unit_extension_data_flag = 0;
}