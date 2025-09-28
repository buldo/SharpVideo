namespace SharpVideo.H264;

/// <summary>
/// A class for parsing out a slice_layer_extension_rbsp NAL unit
/// from an H264 NALU.
/// </summary>
class H264SliceLayerExtensionRbspParser
{
    /// <summary>
    /// Unpack RBSP and parse slice state from the supplied buffer.
    /// </summary>
    public static SliceLayerExtensionRbspState ParseSliceLayerExtensionRbsp(ReadOnlySpan<byte> data, NalUnitHeaderState nal_unit_header, H264BitstreamParserState bitstream_parser_state)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new(unpacked_buffer.ToArray());
        return ParseSliceLayerExtensionRbsp(bit_buffer, nal_unit_header, bitstream_parser_state);
    }

    public static SliceLayerExtensionRbspState ParseSliceLayerExtensionRbsp(
        BitBuffer bit_buffer,
        NalUnitHeaderState nal_unit_header,
        H264BitstreamParserState bitstream_parser_state)
    {
        // H264 slice (slice_layer_extension_rbsp()) NAL Unit.
        // Section 7.3.2.13 ("Slice layer extension RBSP syntax") of
        // the H.264 standard for a complete description.
        var slice_layer_extension_rbsp = new SliceLayerExtensionRbspState();

        // input parameters
        slice_layer_extension_rbsp.svc_extension_flag =
            nal_unit_header.svc_extension_flag;
        slice_layer_extension_rbsp.avc_3d_extension_flag =
            nal_unit_header.avc_3d_extension_flag;
        slice_layer_extension_rbsp.nal_ref_idc = nal_unit_header.nal_ref_idc;
        slice_layer_extension_rbsp.nal_unit_type = nal_unit_header.nal_unit_type;

        if (slice_layer_extension_rbsp.svc_extension_flag != 0)
        {
            // slice_header_in_scalable_extension()  // specified in Annex G
            slice_layer_extension_rbsp.slice_header_in_scalable_extension = H264SliceHeaderInScalableExtensionParser.ParseSliceHeaderInScalableExtension(bit_buffer, nal_unit_header, bitstream_parser_state);
            // if (!slice_skip_flag) {
            //   slice_data_in_scalable_extension()  // specified in Annex G
            // }

        }
        else if (slice_layer_extension_rbsp.avc_3d_extension_flag != 0)
        {
            // slice_header_in_3davc_extension()  // specified in Annex J
            // slice_data_in_3davc_extension()  // specified in Annex J

        }
        else
        {
            // slice_header(slice_type)
            slice_layer_extension_rbsp.slice_header = H264SliceHeaderParser.ParseSliceHeader(bit_buffer, slice_layer_extension_rbsp.nal_ref_idc, slice_layer_extension_rbsp.nal_unit_type, bitstream_parser_state);
            if (slice_layer_extension_rbsp.slice_header == null)
            {
                return null;
            }
            // slice_data()
        }

        // rbsp_slice_trailing_bits()

        return slice_layer_extension_rbsp;
    }
}