namespace SharpVideo.H264;

public static class H264SpsParser
{
    /// <summary>
    /// Unpack RBSP and parse SPS state from the supplied buffer.
    /// </summary>
    public static SpsState ParseSps(ReadOnlySpan<byte> data)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new BitBuffer(unpacked_buffer.ToArray());
        return ParseSps(bit_buffer);
    }
    public static SpsState ParseSps(BitBuffer bit_buffer)
    {
        // H264 SPS Nal Unit (seq_parameter_set_rbsp(()) parser.
        // Section 7.3.2.1 ("Sequence parameter set RBSP syntax") of the H.264
        // standard for a complete description.
        var sps = new SpsState();

        // seq_parameter_set_data()
        sps.sps_data = H264SpsDataParser.ParseSpsData(bit_buffer);
        if (sps.sps_data == null)
        {
            return null;
        }

        H264Common.rbsp_trailing_bits(bit_buffer);

        return sps;
    }
}