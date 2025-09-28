namespace SharpVideo.H264;

/// <summary>
/// A class for parsing out a subset sequence parameter set (SPS) data from an H264 NALU.
/// </summary>
class H264SubsetSpsParser
{
    /// <summary>
    /// Unpack RBSP and parse SPS state from the supplied buffer.
    /// </summary>
    public static SubsetSpsState? ParseSubsetSps(ReadOnlySpan<byte> data)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new(unpacked_buffer.ToArray());
        return ParseSubsetSps(bit_buffer);
    }

    public static SubsetSpsState? ParseSubsetSps(BitBuffer bit_buffer)
    {
        // H264 SPS Nal Unit (subset_seq_parameter_set_rbsp()) parser.
        // Section 7.3.2.1.3 ("Subset sequence parameter set data syntax") of the
        // H.264 standard for a complete description.
        var subset_sps = new SubsetSpsState();

        // seq_parameter_set_data()
        subset_sps.seq_parameter_set_data =
            H264SpsDataParser.ParseSpsData(bit_buffer);
        if (subset_sps.seq_parameter_set_data == null)
        {
            return null;
        }

        if (subset_sps.seq_parameter_set_data.profile_idc == 83 ||
            subset_sps.seq_parameter_set_data.profile_idc == 86)
        {
            uint32_t ChromaArrayType =
                subset_sps.seq_parameter_set_data.getChromaArrayType();
            // seq_parameter_set_svc_extension()  // specified in Annex G
            subset_sps.seq_parameter_set_svc_extension = H264SpsSvcExtensionParser.ParseSpsSvcExtension(bit_buffer, ChromaArrayType);
            if (subset_sps.seq_parameter_set_svc_extension == null)
            {
                return null;
            }

            // svc_vui_parameters_present_flag  u(1)
            if (!bit_buffer.ReadBits(1, out subset_sps.svc_vui_parameters_present_flag))
            {
                return null;
            }

            if (subset_sps.svc_vui_parameters_present_flag == 1)
            {
                // svc_vui_parameters_extension() // specified in Annex G
            }

        }
        else if (subset_sps.seq_parameter_set_data.profile_idc == 118 ||
                   subset_sps.seq_parameter_set_data.profile_idc == 128 ||
                   subset_sps.seq_parameter_set_data.profile_idc == 134)
        {
            // bit_equal_to_one  u(1)
            if (!bit_buffer.ReadBits(1, out subset_sps.bit_equal_to_one))
            {
                return null;
            }

            // seq_parameter_set_mvc_extension() // specified in Annex H

            // mvc_vui_parameters_present_flag  u(1)
            if (!bit_buffer.ReadBits(1, out subset_sps.mvc_vui_parameters_present_flag))
            {
                return null;
            }

            if (subset_sps.mvc_vui_parameters_present_flag == 1)
            {
                // mvc_vui_parameters_extension()  // specified in Annex H
            }

        }
        else if (subset_sps.seq_parameter_set_data.profile_idc == 138 ||
                   subset_sps.seq_parameter_set_data.profile_idc == 135)
        {
            // bit_equal_to_one  u(1)
            if (!bit_buffer.ReadBits(1, out subset_sps.bit_equal_to_one))
            {
                return null;
            }

            // seq_parameter_set_mvcd_extension(()  // specified in Annex I

        }
        else if (subset_sps.seq_parameter_set_data.profile_idc == 139)
        {
            // bit_equal_to_one  u(1)
            if (!bit_buffer.ReadBits(1, out subset_sps.bit_equal_to_one))
            {
                return null;
            }

            // seq_parameter_set_mvcd_extension()  // specified in Annex I

            // seq_parameter_set_3davc_extension()  // specified in Annex J
        }

        // additional_extension2_flag  u(1)
        if (!bit_buffer.ReadBits(1, out subset_sps.additional_extension2_flag))
        {
            return null;
        }

        if (subset_sps.additional_extension2_flag == 1)
        {
            while (H264Common.MoreRbspData(bit_buffer))
            {
                // additional_extension2_data_flag  u(1)
                if (!bit_buffer.ReadBits(1,out subset_sps.additional_extension2_data_flag))
                {
                    return null;
                }
            }
        }

        H264Common.rbsp_trailing_bits(bit_buffer);

        return subset_sps;
    }
}