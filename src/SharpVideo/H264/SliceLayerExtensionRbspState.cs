namespace SharpVideo.H264;

public class SliceLayerExtensionRbspState
{

    // input parameters
    public uint32_t svc_extension_flag = 0;
    public uint32_t avc_3d_extension_flag = 0;
    public uint32_t nal_ref_idc = 0;
    public uint32_t nal_unit_type = 0;

    // contents
    // TODO(chemag): slice_header_in_scalable_extension()
    public SliceHeaderInScalableExtensionState slice_header_in_scalable_extension;
    // slice_data_in_scalable_extension()
    // slice_header_in_3davc_extension()
    // slice_data_in_3davc_extension()
    public SliceHeaderState slice_header;
    // slice_data()

    // rbsp_slice_trailing_bits()
}