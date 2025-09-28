namespace SharpVideo.H264;

/// <summary>
/// A class for parsing out an H264 NAL Unit Header.
/// </summary>
public class H264NalUnitHeaderParser
{
    /// <summary>
    /// Unpack RBSP and parse NAL unit header state from the supplied buffer.
    /// </summary>
    static NalUnitHeaderState? ParseNalUnitHeader(ReadOnlySpan<byte> data)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new(unpacked_buffer.ToArray());

        return ParseNalUnitHeader(bit_buffer);
    }

    public static NalUnitHeaderState? ParseNalUnitHeader(BitBuffer bit_buffer)
    {
        // H264 NAL Unit Header (nal_unit_header()) parser.
        // Section 7.3.1.2 ("NAL unit header syntax") of the H.264
        // standard for a complete description.
        var nal_unit_header = new NalUnitHeaderState();

        // forbidden_zero_bit  f(1)
        if (!bit_buffer.ReadBits(1, out nal_unit_header.forbidden_zero_bit))
        {
            return null;
        }

        // nal_ref_idc  u(2)
        if (!bit_buffer.ReadBits(2, out nal_unit_header.nal_ref_idc))
        {
            return null;
        }

        // nal_unit_type  u(5)
        if (!bit_buffer.ReadBits(5, out nal_unit_header.nal_unit_type))
        {
            return null;
        }

        if (nal_unit_header.nal_unit_type == 14 ||
            nal_unit_header.nal_unit_type == 20 ||
            nal_unit_header.nal_unit_type == 21)
        {
            if (nal_unit_header.nal_unit_type != 21)
            {
                // svc_extension_flag  u(1)
                if (!bit_buffer.ReadBits(1, out nal_unit_header.svc_extension_flag))
                {
                    return null;
                }
                if (nal_unit_header.svc_extension_flag == 1)
                {
                    // nal_unit_header_svc_extension()
                    nal_unit_header.nal_unit_header_svc_extension = H264NalUnitHeaderSvcExtensionParser.ParseNalUnitHeaderSvcExtension(bit_buffer);
                    if (nal_unit_header.nal_unit_header_svc_extension == null)
                    {
                        return null;
                    }
                }

            }
            else
            {
                // avc_3d_extension_flag  u(1)
                if (!bit_buffer.ReadBits(1, out nal_unit_header.avc_3d_extension_flag))
                {
                    return null;
                }
            }
        }

        return nal_unit_header;
    }

    /// <summary>
    /// Parses nalu type from the given buffer
    /// </summary>
    public static bool GetNalUnitType(ReadOnlySpan<byte> data, out NalUnitType naluType)
    {
        BitBuffer bitBuffer = new(data.ToArray());
        var naluHeader = ParseNalUnitHeader(bitBuffer);
        if (naluHeader != null)
        {
            naluType = 0;
            return false;
        }
        naluType = (NalUnitType)(naluHeader.nal_unit_type);
        return true;
    }
}