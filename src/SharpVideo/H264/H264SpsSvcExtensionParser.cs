namespace SharpVideo.H264;

/// <summary>
/// A class for parsing out a SPS SVC extension from an H264 NALU.
/// </summary>
class H264SpsSvcExtensionParser
{

    /// <summary>
    /// Unpack RBSP and parse SPS SVC state from the supplied buffer.
    /// </summary>
    public static SpsSvcExtensionState? ParseSpsSvcExtension(ReadOnlySpan<byte> data, uint32_t ChromaArrayType)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new(unpacked_buffer.ToArray());
        return ParseSpsSvcExtension(bit_buffer, ChromaArrayType);
    }
    public static SpsSvcExtensionState? ParseSpsSvcExtension(BitBuffer bit_buffer, uint32_t ChromaArrayType)
    {
        // H264 seq_parameter_set_svc_extension() parser.
        // Section G.7.3.2.1.4 ("Sequence parameter set SVC extension syntax") of
        // the H.264 standard for a complete description.
        var sps_svc_extension = new SpsSvcExtensionState();

        // store input values
        sps_svc_extension.ChromaArrayType = ChromaArrayType;

        // inter_layer_deblocking_filter_control_present_flag  u(1)
        if (!bit_buffer.ReadBits(1, out sps_svc_extension.inter_layer_deblocking_filter_control_present_flag))
        {
            return null;
        }

        // extended_spatial_scalability_idc  u(2)
        if (!bit_buffer.ReadBits(2, out sps_svc_extension.extended_spatial_scalability_idc))
        {
            return null;
        }

        if (sps_svc_extension.ChromaArrayType == 1 ||
            sps_svc_extension.ChromaArrayType == 2)
        {
            // chroma_phase_x_plus1_flag  u(1)
            if (!bit_buffer.ReadBits(1, out sps_svc_extension.chroma_phase_x_plus1_flag))
            {
                return null;
            }
        }

        if (sps_svc_extension.ChromaArrayType == 1)
        {
            // chroma_phase_y_plus1  u(2)
            if (!bit_buffer.ReadBits(2, out sps_svc_extension.chroma_phase_y_plus1))
            {
                return null;
            }
        }

        if (sps_svc_extension.extended_spatial_scalability_idc == 1)
        {
            if (sps_svc_extension.ChromaArrayType > 0)
            {
                // seq_ref_layer_chroma_phase_x_plus1_flag  u(1)
                if (!bit_buffer.ReadBits(1, out sps_svc_extension.seq_ref_layer_chroma_phase_x_plus1_flag))
                {
                    return null;
                }

                // seq_ref_layer_chroma_phase_y_plus1  u(2)
                if (!bit_buffer.ReadBits(2, out sps_svc_extension.seq_ref_layer_chroma_phase_y_plus1))
                {
                    return null;
                }
            }

            // seq_scaled_ref_layer_left_offset  se(v)
            if (!bit_buffer.ReadSignedExponentialGolomb(out sps_svc_extension.seq_scaled_ref_layer_left_offset))
            {
                return null;
            }

            // seq_scaled_ref_layer_top_offset  se(v)
            if (!bit_buffer.ReadSignedExponentialGolomb(out sps_svc_extension.seq_scaled_ref_layer_top_offset))
            {
                return null;
            }

            // seq_scaled_ref_layer_right_offset  se(v)
            if (!bit_buffer.ReadSignedExponentialGolomb(out sps_svc_extension.seq_scaled_ref_layer_right_offset))
            {
                return null;
            }

            // seq_scaled_ref_layer_bottom_offset  se(v)
            if (!bit_buffer.ReadSignedExponentialGolomb(out sps_svc_extension.seq_scaled_ref_layer_bottom_offset))
            {
                return null;
            }
        }

        // seq_tcoeff_level_prediction_flag  u(1)
        if (!bit_buffer.ReadBits(1, out sps_svc_extension.seq_tcoeff_level_prediction_flag))
        {
            return null;
        }

        if (sps_svc_extension.seq_tcoeff_level_prediction_flag == 1)
        {
            // adaptive_tcoeff_level_prediction_flag  u(1)
            if (!bit_buffer.ReadBits(1, out sps_svc_extension.adaptive_tcoeff_level_prediction_flag))
            {
                return null;
            }
        }

        // slice_header_restriction_flag  u(1)
        if (!bit_buffer.ReadBits(1, out sps_svc_extension.slice_header_restriction_flag))
        {
            return null;
        }

        return sps_svc_extension;
    }
}