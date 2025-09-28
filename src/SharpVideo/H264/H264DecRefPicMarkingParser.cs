namespace SharpVideo.H264;

/// <summary>
/// A class for parsing out a decoded reference picture list marking
/// (`dec_ref_pic_marking()`, as defined in Section 7.3.3.3 of the 2012
/// standard) from an H264 NALU.
/// </summary>
class H264DecRefPicMarkingParser
{
    /// <summary>
    /// Unpack RBSP and parse DecRefPicMarking state from the supplied buffer.
    /// </summary>
    static DecRefPicMarkingState? ParseDecRefPicMarking(ReadOnlySpan<byte> data, uint32_t nal_unit_type)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer =new(unpacked_buffer.ToArray());
        return ParseDecRefPicMarking(bit_buffer, nal_unit_type);
    }

    public static DecRefPicMarkingState? ParseDecRefPicMarking(BitBuffer bit_buffer, uint32_t nal_unit_type)
    {
        uint32_t golomb_tmp;

        // H264 dec_ref_pic_marking() NAL Unit.
        // Section 7.3.3.3 ("Decoded reference picture marking syntax") of the
        // H.264 standard for a complete description.
        var dec_ref_pic_marking = new DecRefPicMarkingState();

        // store input values
        dec_ref_pic_marking.nal_unit_type = nal_unit_type;

        // Equation (7-1)
        bool IdrPicFlag = ((nal_unit_type == 5) ? true : false);

        if (IdrPicFlag)
        {
            // no_output_of_prior_pics_flag  u(1)
            if (!bit_buffer.ReadBits(
                    1, out (dec_ref_pic_marking.no_output_of_prior_pics_flag)))
            {
                return null;
            }

            // long_term_reference_flag  u(1)
            if (!bit_buffer.ReadBits(
                    1, out (dec_ref_pic_marking.long_term_reference_flag)))
            {
                return null;
            }

        }
        else
        {
            // adaptive_ref_pic_marking_mode_flag  u(1)
            if (!bit_buffer.ReadBits(
                    1, out (dec_ref_pic_marking.adaptive_ref_pic_marking_mode_flag)))
            {
                return null;
            }

            if (dec_ref_pic_marking.adaptive_ref_pic_marking_mode_flag != 0)
            {
                do
                {
                    // memory_management_control_operation[i]  ue(v)
                    if (!bit_buffer.ReadExponentialGolomb(out golomb_tmp))
                    {
                        return null;
                    }

                    dec_ref_pic_marking.memory_management_control_operation.Add(
                        golomb_tmp);

                    if ((dec_ref_pic_marking.memory_management_control_operation.Last() == 1) ||
                        (dec_ref_pic_marking.memory_management_control_operation.Last() == 3))
                    {
                        // difference_of_pic_nums_minus1[i]  ue(v)
                        if (!bit_buffer.ReadExponentialGolomb(out golomb_tmp))
                        {
                            return null;
                        }

                        dec_ref_pic_marking.difference_of_pic_nums_minus1.Add(golomb_tmp);
                    }

                    if (dec_ref_pic_marking.memory_management_control_operation.Last() == 2)
                    {
                        // long_term_pic_num[i]  ue(v)
                        if (!bit_buffer.ReadExponentialGolomb(out golomb_tmp))
                        {
                            return null;
                        }

                        dec_ref_pic_marking.long_term_pic_num.Add(golomb_tmp);
                    }

                    if ((dec_ref_pic_marking.memory_management_control_operation.Last() == 3) ||
                        (dec_ref_pic_marking.memory_management_control_operation.Last() == 6))
                    {
                        // long_term_frame_idx[i]  ue(v)
                        if (!bit_buffer.ReadExponentialGolomb(out golomb_tmp))
                        {
                            return null;
                        }

                        dec_ref_pic_marking.long_term_frame_idx.Add(golomb_tmp);
                    }

                    if (dec_ref_pic_marking.memory_management_control_operation.Last() == 4)
                    {
                        // max_long_term_frame_idx_plus1[i]  ue(v)
                        if (!bit_buffer.ReadExponentialGolomb(out golomb_tmp))
                        {
                            return null;
                        }

                        dec_ref_pic_marking.max_long_term_frame_idx_plus1.Add(
                            golomb_tmp);
                    }
                } while (
                    dec_ref_pic_marking.memory_management_control_operation.Last() != 0);
            }
        }

        return dec_ref_pic_marking;
    }
}