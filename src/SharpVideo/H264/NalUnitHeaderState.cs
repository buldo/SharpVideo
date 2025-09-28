namespace SharpVideo.H264;

/// <summary>
/// The parsed state of the NAL Unit Header.
/// </summary>
public class NalUnitHeaderState
{

    public uint32_t forbidden_zero_bit = 0;
    public uint32_t nal_ref_idc = 0;
    public uint32_t nal_unit_type = 0;
    public uint32_t svc_extension_flag = 0;
    public uint32_t avc_3d_extension_flag = 0;
    public NalUnitHeaderSvcExtensionState nal_unit_header_svc_extension;
    // TODO(chema): nal_unit_header_3davc_extension()  // specified in Annex J
    // TODO(chema): nal_unit_header_mvc_extension()  // specified in Annex H
}