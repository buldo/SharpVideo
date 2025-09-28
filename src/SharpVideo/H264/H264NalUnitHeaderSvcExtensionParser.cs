namespace SharpVideo.H264;

/// <summary>
/// A class for parsing out an H264 NAL Unit Header SVC Extension.
/// </summary>
class H264NalUnitHeaderSvcExtensionParser
{
    /// <summary>
    /// Unpack RBSP and parse NAL unit header state from the supplied buffer.
    /// </summary>
    public NalUnitHeaderSvcExtensionState? ParseNalUnitHeaderSvcExtension(ReadOnlySpan<byte> data)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new(unpacked_buffer.ToArray());

        return ParseNalUnitHeaderSvcExtension(bit_buffer);
    }
    public static NalUnitHeaderSvcExtensionState? ParseNalUnitHeaderSvcExtension(BitBuffer bit_buffer)
    {
        // H264 NAL Unit Header SVC Extension (nal_unit_header_svc_extension())
        // parser.
        // Section G.7.3.1.1 ("NAL unit header SVC Extension syntax") of the H.264
        // standard for a complete description.
        var nal_unit_header = new NalUnitHeaderSvcExtensionState();

        // idr_flag  u(1)
        if (!bit_buffer.ReadBits(1, out nal_unit_header.idr_flag))
        {
            return null;
        }

        // priority_id  u(6)
        if (!bit_buffer.ReadBits(6, out nal_unit_header.priority_id))
        {
            return null;
        }

        // no_inter_layer_pred_flag  u(1)
        if (!bit_buffer.ReadBits(1, out nal_unit_header.no_inter_layer_pred_flag))
        {
            return null;
        }

        // dependency_id  u(3)
        if (!bit_buffer.ReadBits(3, out nal_unit_header.dependency_id))
        {
            return null;
        }

        // quality_id  u(4)
        if (!bit_buffer.ReadBits(4, out nal_unit_header.quality_id))
        {
            return null;
        }

        // temporal_id  u(3)
        if (!bit_buffer.ReadBits(3, out nal_unit_header.temporal_id))
        {
            return null;
        }

        // use_ref_base_pic_flag  u(1)
        if (!bit_buffer.ReadBits(1, out nal_unit_header.use_ref_base_pic_flag))
        {
            return null;
        }

        // discardable_flag  u(1)
        if (!bit_buffer.ReadBits(1, out nal_unit_header.discardable_flag))
        {
            return null;
        }

        // output_flag  u(1)
        if (!bit_buffer.ReadBits(1, out nal_unit_header.output_flag))
        {
            return null;
        }

        // reserved_three_2bits  u(2)
        if (!bit_buffer.ReadBits(2, out nal_unit_header.reserved_three_2bits))
        {
            return null;
        }

        return nal_unit_header;
    }
}