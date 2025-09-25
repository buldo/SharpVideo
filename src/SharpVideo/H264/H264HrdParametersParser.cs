namespace SharpVideo.H264;

/// <summary>
/// A class for parsing out a HRD parameters (`hrd_parameters()`, as defined in Section E.1.2 of the 2012 standard) from an H264 NALU.
/// </summary>
public class H264HrdParametersParser
{
    // Section E.2.2: "The value of cpb_cnt_minus1 shall be in the range of
    // 0 to 31, inclusive."
    const UInt32 kCpbCntMinus1Min = 0;
    const UInt32 kCpbCntMinus1Max = 31;

    /// <summary>
    /// Unpack RBSP and parse HrdParameters state from the supplied buffer.
    /// </summary>
    public static HrdParametersState ParseHrdParameters(ReadOnlySpan<byte> data)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new BitBuffer(unpacked_buffer.ToArray());
        return ParseHrdParameters(bit_buffer);
    }

    public static HrdParametersState? ParseHrdParameters(BitBuffer bit_buffer)
    {
        UInt32 bits_tmp;
        UInt32 golomb_tmp;

        // H264 hrd_parameters() NAL Unit.
        // Section E.1.2. ("HRD parameters syntax") of the
        // H.264 standard for a complete description.
        var hrd_parameters = new HrdParametersState();

        // cpb_cnt_minus1[i]  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out hrd_parameters.cpb_cnt_minus1))
        {
            return null;
        }
        if (hrd_parameters.cpb_cnt_minus1 < kCpbCntMinus1Min ||
            hrd_parameters.cpb_cnt_minus1 > kCpbCntMinus1Max)
        {
#if DEBUG
            //fprintf(stderr, "invalid cpb_cnt_minus1: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", hrd_parameters.cpb_cnt_minus1, kCpbCntMinus1Min, kCpbCntMinus1Max);
#endif  // FPRINT_ERRORS
            return null;
        }

        // bit_rate_scale  u(4)
        if (!bit_buffer.ReadBits(4, out (hrd_parameters.bit_rate_scale)))
        {
            return null;
        }

        // cpb_size_scale  u(4)
        if (!bit_buffer.ReadBits(4, out (hrd_parameters.cpb_size_scale)))
        {
            return null;
        }

        for (UInt32 SchedSelIdx = 0; SchedSelIdx <= hrd_parameters.cpb_cnt_minus1;
             SchedSelIdx++)
        {
            // bit_rate_value_minus1[i]  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out golomb_tmp))
            {
                return null;
            }
            hrd_parameters.bit_rate_value_minus1.Add(golomb_tmp);

            // cpb_size_value_minus1[i]  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out golomb_tmp))
            {
                return null;
            }
            hrd_parameters.cpb_size_value_minus1.Add(golomb_tmp);

            // cbr_flag[i]  u(1)
            if (!bit_buffer.ReadBits(1, out bits_tmp))
            {
                return null;
            }
            hrd_parameters.cbr_flag.Add(bits_tmp);
        }

        // initial_cpb_removal_delay_length_minus1  u(5)
        if (!bit_buffer.ReadBits(5, out (hrd_parameters.initial_cpb_removal_delay_length_minus1)))
        {
            return null;
        }

        // cpb_removal_delay_length_minus1  u(5)
        if (!bit_buffer.ReadBits(5, out (hrd_parameters.cpb_removal_delay_length_minus1)))
        {
            return null;
        }

        // dpb_output_delay_length_minus1  u(5)
        if (!bit_buffer.ReadBits(5, out (hrd_parameters.dpb_output_delay_length_minus1)))
        {
            return null;
        }

        // time_offset_length  u(5)
        if (!bit_buffer.ReadBits(5, out (hrd_parameters.time_offset_length)))
        {
            return null;
        }

        return hrd_parameters;
    }
}