namespace SharpVideo.H264;

/// <summary>
/// A class for parsing out an H264 NAL Unit Payload.
/// </summary>
class H264NalUnitPayloadParser
{

    /// <summary>
    /// Unpack RBSP and parse NAL unit payload state from the supplied buffer.
    /// </summary>
    public static NalUnitPayloadState ParseNalUnitPayload(ReadOnlySpan<byte> data, NalUnitHeaderState nal_unit_header, H264BitstreamParserState bitstream_parser_state)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new(unpacked_buffer.ToArray());

        return ParseNalUnitPayload(bit_buffer, nal_unit_header, bitstream_parser_state);
    }

    public static NalUnitPayloadState ParseNalUnitPayload(BitBuffer bit_buffer, NalUnitHeaderState nal_unit_header, H264BitstreamParserState bitstream_parser_state)
    {
        // H264 NAL Unit Payload (nal_unit()) parser.
        // Section 7.3.1 ("NAL unit syntax") of the H.264
        // standard for a complete description.
        var nal_unit_payload = new NalUnitPayloadState();

        // payload (Table 7-1, Section 7.4.1)
        switch ((NalUnitType)nal_unit_header.nal_unit_type)
        {
            case NalUnitType.CODED_SLICE_OF_NON_IDR_PICTURE_NUT:
                {
                    // slice_layer_without_partitioning_rbsp()
                    nal_unit_payload.slice_layer_without_partitioning_rbsp = H264SliceLayerWithoutPartitioningRbspParser
                        .ParseSliceLayerWithoutPartitioningRbsp(bit_buffer, nal_unit_header.nal_ref_idc, nal_unit_header.nal_unit_type, bitstream_parser_state);
                    break;
                }
            case NalUnitType.CODED_SLICE_DATA_PARTITION_A_NUT:
            case NalUnitType.CODED_SLICE_DATA_PARTITION_B_NUT:
            case NalUnitType.CODED_SLICE_DATA_PARTITION_C_NUT:
                // unimplemented
                break;
            case NalUnitType.CODED_SLICE_OF_IDR_PICTURE_NUT:
                {
                    // slice_layer_without_partitioning_rbsp()
                    nal_unit_payload.slice_layer_without_partitioning_rbsp =
                        H264SliceLayerWithoutPartitioningRbspParser.
                            ParseSliceLayerWithoutPartitioningRbsp(bit_buffer, nal_unit_header.nal_ref_idc, nal_unit_header.nal_unit_type, bitstream_parser_state);
                    break;
                }
            case NalUnitType.SEI_NUT:
                // unimplemented
                break;
            case NalUnitType.SPS_NUT:
                {
                    // seq_parameter_set_rbsp()
                    nal_unit_payload.sps = H264SpsParser.ParseSps(bit_buffer);
                    if (nal_unit_payload.sps != null)
                    {
                        uint32_t sps_id = nal_unit_payload.sps.sps_data.seq_parameter_set_id;
                        bitstream_parser_state.sps[sps_id] = nal_unit_payload.sps;
                    }
                    break;
                }
            case NalUnitType.PPS_NUT:
                {
                    // pic_parameter_set_rbsp()
                    // TODO(chemag): Fix this
                    // This is a big pita in the h264 standard. The dependency on
                    // this field (`chroma_format_idc`, defined in the SPS) forces the
                    // parser to pass the full SPS map to the PPS parser: This would
                    // allow the PPS parser to first read the `seq_parameter_set_id`
                    // field, and then uses it with the SPS map to get the right
                    // `chroma_format_idc` value tp use.
                    // Unfortunately the SPS map is part of the BitstreamParserState,
                    // which itself depends on the PPS. This creates a circular
                    // dependency.
                    // A better solution would be to pass the SPS map to the PPS
                    // parser. This increases significantly the complexity of the
                    // parser, and the usage of the `chroma_format_idc` field
                    // For now, let's use the most common value, which corresponds
                    // to 4:2:0 subsampling.
                    uint32_t chroma_format_idc = 1;
                    nal_unit_payload.pps = H264PpsParser.ParsePps(bit_buffer, chroma_format_idc);
                    if (nal_unit_payload.pps != null)
                    {
                        uint32_t pps_id = nal_unit_payload.pps.pic_parameter_set_id;
                        bitstream_parser_state.pps[pps_id] = nal_unit_payload.pps;
                    }
                    break;
                }
            case NalUnitType.AUD_NUT:
            case NalUnitType.EOSEQ_NUT:
            case NalUnitType.EOSTREAM_NUT:
            case NalUnitType.FILLER_DATA_NUT:
            case NalUnitType.SPS_EXTENSION_NUT:
                // unimplemented
                break;
            case NalUnitType.PREFIX_NUT:
                {
                    // prefix_nal_unit_rbsp()
                    if (nal_unit_header.svc_extension_flag != 0 &&
                        nal_unit_header.nal_unit_header_svc_extension != null)
                    {
                        uint32_t use_ref_base_pic_flag =
                            nal_unit_header.nal_unit_header_svc_extension
                                .use_ref_base_pic_flag;
                        uint32_t idr_flag =
                            nal_unit_header.nal_unit_header_svc_extension.idr_flag;
                        nal_unit_payload.prefix_nal_unit =
                            H264PrefixNalUnitRbspParser.ParsePrefixNalUnitRbsp(
                                bit_buffer, nal_unit_header.svc_extension_flag,
                                nal_unit_header.nal_ref_idc, use_ref_base_pic_flag, idr_flag);
                    }
                    break;
                }
            case NalUnitType.SUBSET_SPS_NUT:
                {
                    // subset_seq_parameter_set_rbsp()
                    nal_unit_payload.subset_sps =
                        H264SubsetSpsParser.ParseSubsetSps(bit_buffer);
                    // add subset_sps to bitstream_parser_state.subset_sps
                    if (nal_unit_payload.subset_sps != null)
                    {
                        uint32_t subset_sps_id =
                            nal_unit_payload.subset_sps.seq_parameter_set_data
                                .seq_parameter_set_id;
                        bitstream_parser_state.subset_sps[subset_sps_id] = nal_unit_payload.subset_sps;
                    }
                    break;
                }
            case NalUnitType.RSV16_NUT:
            case NalUnitType.RSV17_NUT:
            case NalUnitType.RSV18_NUT:
                // reserved
                break;
            case NalUnitType.CODED_SLICE_OF_AUXILIARY_CODED_PICTURE_NUT:
                // unimplemented
                break;
            case NalUnitType.CODED_SLICE_EXTENSION:
                // slice_layer_extension_rbsp()
                nal_unit_payload.slice_layer_extension_rbsp = H264SliceLayerExtensionRbspParser.ParseSliceLayerExtensionRbsp(bit_buffer, nal_unit_header, bitstream_parser_state);
                break;
            case NalUnitType.RSV21_NUT:
            case NalUnitType.RSV22_NUT:
            case NalUnitType.RSV23_NUT:
                // reserved
                break;
            case NalUnitType.UNSPECIFIED_NUT:
            case NalUnitType.UNSPEC24_NUT:
            case NalUnitType.UNSPEC25_NUT:
            case NalUnitType.UNSPEC26_NUT:
            case NalUnitType.UNSPEC27_NUT:
            case NalUnitType.UNSPEC28_NUT:
            case NalUnitType.UNSPEC29_NUT:
            case NalUnitType.UNSPEC30_NUT:
            case NalUnitType.UNSPEC31_NUT:
            default:
                // unspecified
                break;
        }

        return nal_unit_payload;
    }


    /// <summary>
    /// used by RTP fu-a, which has a pseudo-NALU header
    /// </summary>
    public static NalUnitPayloadState ParseNalUnitPayload(BitBuffer bit_buffer, uint32_t nal_ref_idc, uint32_t nal_unit_type,H264BitstreamParserState bitstream_parser_state)
    {
        NalUnitHeaderState nal_unit_header = new();
        nal_unit_header.forbidden_zero_bit = 0;
        nal_unit_header.nal_ref_idc = nal_ref_idc;
        nal_unit_header.nal_unit_type = nal_unit_type;
        nal_unit_header.svc_extension_flag = 0;
        nal_unit_header.avc_3d_extension_flag = 0;
        return ParseNalUnitPayload(bit_buffer, nal_unit_header, bitstream_parser_state);
    }
}