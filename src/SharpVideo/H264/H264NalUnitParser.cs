namespace SharpVideo.H264;

/// <summary>
/// A class for parsing out an H264 NAL Unit.
/// </summary>
public static class H264NalUnitParser
{
    /// <summary>
    /// Parse NAL unit state from the supplied buffer.
    /// Use this function to parse NALUs that have not been escaped
    /// into an RBSP, e.g. with NALUs from an mp4 mdat box.
    /// </summary>
    public static NalUnitState ParseNalUnitUnescaped(ReadOnlySpan<byte> data, int length, H264BitstreamParserState bitstream_parser_state, ParsingOptions parsing_options)
    {
        BitBuffer bit_buffer = new(data.ToArray());

        return ParseNalUnit(bit_buffer, bitstream_parser_state, parsing_options);
    }

    /// <summary>
    /// Unpack RBSP and parse NAL unit state from the supplied buffer.
    /// Use this function to parse NALUs that have been escaped
    /// to avoid the start code prefix (0x000001/0x00000001)
    /// </summary>
    /// <returns></returns>
    public static NalUnitState ParseNalUnit(ReadOnlySpan<byte> data, H264BitstreamParserState bitstream_parser_state,ParsingOptions parsing_options)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new(unpacked_buffer.ToArray());

        return ParseNalUnit(bit_buffer, bitstream_parser_state, parsing_options);
    }

    public static NalUnitState? ParseNalUnit(
        BitBuffer bit_buffer,
        H264BitstreamParserState bitstream_parser_state,
        ParsingOptions parsing_options)
    {
        // H264 NAL Unit (nal_unit()) parser.
        // Section 7.3.1 ("NAL unit syntax") of the H.264
        // standard for a complete description.
        var nal_unit = new NalUnitState();

        // need to calculate the checksum before parsing the bit buffer
        if (parsing_options.add_checksum)
        {
            // set the checksum
            nal_unit.checksum = NaluChecksum.GetNaluChecksum(bit_buffer);
        }

        // nal_unit_header()
        nal_unit.nal_unit_header = H264NalUnitHeaderParser.ParseNalUnitHeader(bit_buffer);
        if (nal_unit.nal_unit_header == null)
        {
#if DEBUG
            //fprintf(stderr, "error: cannot ParseNalUnitHeader in nal unit\n");
#endif // FPRINT_ERRORS
            return null;
        }

        // nal_unit_payload()
        nal_unit.nal_unit_payload =
            H264NalUnitPayloadParser.ParseNalUnitPayload(bit_buffer, nal_unit.nal_unit_header, bitstream_parser_state);
        if (nal_unit.nal_unit_payload == null)
        {
            return null;
        }

        // update the parsed length
        nal_unit.parsed_length = H264Common.get_current_offset(bit_buffer);

        return nal_unit;
    }
}