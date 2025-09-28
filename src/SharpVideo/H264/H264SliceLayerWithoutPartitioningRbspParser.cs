namespace SharpVideo.H264;

/// <summary>
/// A class for parsing out a slice_layer_without_partitioning_rbsp NAL unit
/// from an H264 NALU.
/// </summary>
class H264SliceLayerWithoutPartitioningRbspParser
{
    /// <summary>
    /// Unpack RBSP and parse slice state from the supplied buffer.
    /// </summary>
    public static SliceLayerWithoutPartitioningRbspState ParseSliceLayerWithoutPartitioningRbsp(ReadOnlySpan<byte> data,
        uint32_t nal_ref_idc, uint32_t nal_unit_type, H264BitstreamParserState bitstream_parser_state)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new(unpacked_buffer.ToArray());
        return ParseSliceLayerWithoutPartitioningRbsp(bit_buffer, nal_ref_idc, nal_unit_type, bitstream_parser_state);
    }

    public static SliceLayerWithoutPartitioningRbspState ParseSliceLayerWithoutPartitioningRbsp(
        BitBuffer bit_buffer,
        uint32_t nal_ref_idc,
        uint32_t nal_unit_type,
        H264BitstreamParserState bitstream_parser_state)
    {
        // H264 slice (slice_layer_without_partitioning_rbsp()) NAL Unit.
        // Section 7.3.2.8 ("Slice layer without partitioning RBSP syntax") of
        // the H.264 standard for a complete description.
        var slice_layer_without_partitioning_rbsp = new SliceLayerWithoutPartitioningRbspState();

        // input parameters
        slice_layer_without_partitioning_rbsp.nal_ref_idc = nal_ref_idc;
        slice_layer_without_partitioning_rbsp.nal_unit_type = nal_unit_type;

        // slice_header(slice_type)
        slice_layer_without_partitioning_rbsp.slice_header =
            H264SliceHeaderParser.ParseSliceHeader(
                bit_buffer, slice_layer_without_partitioning_rbsp.nal_ref_idc,
                slice_layer_without_partitioning_rbsp.nal_unit_type,
                bitstream_parser_state);
        if (slice_layer_without_partitioning_rbsp.slice_header == null)
        {
            return null;
        }

        // slice_data()
        // rbsp_slice_trailing_bits()

        return slice_layer_without_partitioning_rbsp;
    }
}