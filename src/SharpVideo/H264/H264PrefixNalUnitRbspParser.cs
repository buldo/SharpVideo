namespace SharpVideo.H264;

/// <summary>
/// prefix_nal_unit_rbsp()
/// </summary>
public class H264PrefixNalUnitRbspParser
{
    /// <summary>
    /// Unpack RBSP and parse prefix_nal_unit_rbsp() state from the supplied buffer.
    /// </summary>
    public static PrefixNalUnitRbspState? ParsePrefixNalUnitRbsp(ReadOnlySpan<byte> data, uint32_t svc_extension_flag, uint32_t nal_ref_idc, uint32_t use_ref_base_pic_flag, uint32_t idr_flag)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new(unpacked_buffer.ToArray());
        return ParsePrefixNalUnitRbsp(
            bit_buffer,
            svc_extension_flag,
            nal_ref_idc,
            use_ref_base_pic_flag,
            idr_flag);
    }
    public static PrefixNalUnitRbspState? ParsePrefixNalUnitRbsp(BitBuffer bit_buffer, uint32_t svc_extension_flag, uint32_t nal_ref_idc, uint32_t use_ref_base_pic_flag, uint32_t idr_flag)
    {
        // H264 Prefix NAL unit RBSP syntax (prefix_nal_unit_rbsp()) parser.
        // Section 7.3.2.12 ("Prefix NAL unit RBSP syntax") of the H.264
        // standard for a complete description.
        var prefix_nal_unit_rbsp = new PrefixNalUnitRbspState();

        // input parameters
        prefix_nal_unit_rbsp.svc_extension_flag = svc_extension_flag;
        prefix_nal_unit_rbsp.nal_ref_idc = nal_ref_idc;
        prefix_nal_unit_rbsp.use_ref_base_pic_flag = use_ref_base_pic_flag;
        prefix_nal_unit_rbsp.idr_flag = idr_flag;

        if (prefix_nal_unit_rbsp.svc_extension_flag == 1)
        {
            // prefix_nal_unit_svc()
            prefix_nal_unit_rbsp.prefix_nal_unit_svc =
                H264PrefixNalUnitSvcParser.ParsePrefixNalUnitSvc(
                    bit_buffer, prefix_nal_unit_rbsp.nal_ref_idc,
                    prefix_nal_unit_rbsp.use_ref_base_pic_flag,
                    prefix_nal_unit_rbsp.idr_flag);
            if (prefix_nal_unit_rbsp.prefix_nal_unit_svc == null)
            {
                return null;
            }
        }

        return prefix_nal_unit_rbsp;
    }
}