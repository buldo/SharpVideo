namespace SharpVideo.H264;

class H264PrefixNalUnitSvcParser
{
    /// <summary>
    /// Unpack RBSP and parse prefix_nal_unit_svc() state from the supplied buffer.
    /// </summary>
    public static PrefixNalUnitSvcState? ParsePrefixNalUnitSvc(ReadOnlySpan<byte> data, uint32_t nal_ref_idc,
        uint32_t use_ref_base_pic_flag, uint32_t idr_flag)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new(unpacked_buffer.ToArray());
        return ParsePrefixNalUnitSvc(bit_buffer, nal_ref_idc, use_ref_base_pic_flag, idr_flag);
    }

    public static PrefixNalUnitSvcState? ParsePrefixNalUnitSvc(BitBuffer bit_buffer, uint32_t nal_ref_idc,
        uint32_t use_ref_base_pic_flag, uint32_t idr_flag)
    {
        // H264 Prefix NAL unit SVC (prefix_nal_unit_svc()) parser.
        // Section G.7.3.2.12.1 ("Prefix NAL unit SVC syntax") of the H.264
        // standard for a complete description.
        var prefix_nal_unit_svc = new PrefixNalUnitSvcState();

        // input parameters
        prefix_nal_unit_svc.nal_ref_idc = nal_ref_idc;
        prefix_nal_unit_svc.use_ref_base_pic_flag = use_ref_base_pic_flag;
        prefix_nal_unit_svc.idr_flag = idr_flag;

        if (prefix_nal_unit_svc.nal_ref_idc != 0)
        {
            // store_ref_base_pic_flag  u(1)
            if (!bit_buffer.ReadBits(1, out prefix_nal_unit_svc.store_ref_base_pic_flag))
            {
                return null;
            }

            if ((prefix_nal_unit_svc.use_ref_base_pic_flag != 0 ||
                 prefix_nal_unit_svc.store_ref_base_pic_flag != 0) &&
                prefix_nal_unit_svc.idr_flag == 0)
            {
                // dec_ref_base_pic_marking()
#if DEBUG
                //fprintf(stderr, "error: dec_ref_base_pic_marking undefined\n");
#endif // FPRINT_ERRORS
                return null;
            }

            // additional_prefix_nal_unit_extension_flag  u(1)
            if (!bit_buffer.ReadBits(1, out prefix_nal_unit_svc.additional_prefix_nal_unit_extension_flag))
            {
                return null;
            }

            if (prefix_nal_unit_svc.additional_prefix_nal_unit_extension_flag == 1)
            {
                while (H264Common.MoreRbspData(bit_buffer))
                {
                    // additional_prefix_nal_unit_extension_data_flag  u(1)
                    if (!bit_buffer.ReadBits(1, out prefix_nal_unit_svc.additional_prefix_nal_unit_extension_data_flag))
                    {
                        return null;
                    }
                }

                H264Common.rbsp_trailing_bits(bit_buffer);

            }
            else if (H264Common.MoreRbspData(bit_buffer))
            {
                while (H264Common.MoreRbspData(bit_buffer))
                {
                    // additional_prefix_nal_unit_extension_data_flag  u(1)
                    if (!bit_buffer.ReadBits(1, out prefix_nal_unit_svc.additional_prefix_nal_unit_extension_data_flag))
                    {
                        return null;
                    }
                }

                H264Common.rbsp_trailing_bits(bit_buffer);
            }
        }

        return prefix_nal_unit_svc;
    }
}