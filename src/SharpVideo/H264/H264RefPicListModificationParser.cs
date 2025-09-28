namespace SharpVideo.H264;

/// <summary>
/// A class for parsing out a reference picture list modification (`ref_pic_list_modification()`, as defined in Section 7.3.3.1 of the 2012 standard) from an H264 NALU.
/// </summary>
public class H264RefPicListModificationParser
{
    /// <summary>
    /// Unpack RBSP and parse RefPicListModification state from the supplied
    /// buffer.
    /// </summary>
    static RefPicListModificationState? ParseRefPicListModification(Span<byte> data, uint32_t slice_type)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new(unpacked_buffer.ToArray());
        return ParseRefPicListModification(bit_buffer, slice_type);
    }

    public static RefPicListModificationState? ParseRefPicListModification(BitBuffer bit_buffer, uint32_t slice_type)
    {
        uint32_t golomb_tmp;

        // H264 ref_pic_list_modification() NAL Unit.
        // Section 7.3.3.1 ("Reference picture list modification syntax") of the
        // H.264 standard for a complete description.
        var ref_pic_list_modification = new RefPicListModificationState();

        // store input values
        ref_pic_list_modification.slice_type = slice_type;

        if (((slice_type % 5) != 2) && ((slice_type % 5) != 4))
        {
            // ref_pic_list_modification_flag_l0  u(1)
            if (!bit_buffer.ReadBits(1, out (ref_pic_list_modification.ref_pic_list_modification_flag_l0)))
            {
                return null;
            }

            if (ref_pic_list_modification.ref_pic_list_modification_flag_l0 != 0)
            {
                do
                {
                    // modification_of_pic_nums_idc[i]  ue(v)
                    if (!bit_buffer.ReadExponentialGolomb(out golomb_tmp))
                    {
                        return null;
                    }

                    ref_pic_list_modification.modification_of_pic_nums_idc.Add(golomb_tmp);

                    if ((ref_pic_list_modification.modification_of_pic_nums_idc.Last() == 0) ||
                        (ref_pic_list_modification.modification_of_pic_nums_idc.Last() == 1))
                    {
                        // abs_diff_pic_num_minus1[i]  ue(v)
                        if (!bit_buffer.ReadExponentialGolomb(out golomb_tmp))
                        {
                            return null;
                        }

                        ref_pic_list_modification.abs_diff_pic_num_minus1.Add(
                            golomb_tmp);

                    }
                    else if (ref_pic_list_modification.modification_of_pic_nums_idc.Last() == 2)
                    {
                        // long_term_pic_num[i]  ue(v)
                        if (!bit_buffer.ReadExponentialGolomb(out golomb_tmp))
                        {
                            return null;
                        }

                        ref_pic_list_modification.long_term_pic_num.Add(golomb_tmp);
                    }
                } while (ref_pic_list_modification.modification_of_pic_nums_idc.Last() != 3);
            }
        }

        if ((slice_type % 5) == 1)
        {
            // ref_pic_list_modification_flag_l1  u(1)
            if (!bit_buffer.ReadBits(1, out (ref_pic_list_modification.ref_pic_list_modification_flag_l1)))
            {
                return null;
            }

            if (ref_pic_list_modification.ref_pic_list_modification_flag_l1 != 0)
            {
                do
                {
                    // modification_of_pic_nums_idc[i]  ue(v)
                    if (!bit_buffer.ReadExponentialGolomb(out golomb_tmp))
                    {
                        return null;
                    }

                    ref_pic_list_modification.modification_of_pic_nums_idc.Add(golomb_tmp);

                    if ((ref_pic_list_modification.modification_of_pic_nums_idc.Last() == 0) ||
                        (ref_pic_list_modification.modification_of_pic_nums_idc.Last() == 1))
                    {
                        // abs_diff_pic_num_minus1[i]  ue(v)
                        if (!bit_buffer.ReadExponentialGolomb(out golomb_tmp))
                        {
                            return null;
                        }

                        ref_pic_list_modification.abs_diff_pic_num_minus1.Add(golomb_tmp);

                    }
                    else if (ref_pic_list_modification.modification_of_pic_nums_idc.Last() == 2)
                    {
                        // long_term_pic_num[i]  ue(v)
                        if (!bit_buffer.ReadExponentialGolomb(out golomb_tmp))
                        {
                            return null;
                        }

                        ref_pic_list_modification.long_term_pic_num.Add(golomb_tmp);
                    }
                } while (ref_pic_list_modification.modification_of_pic_nums_idc.Last() != 3);
            }
        }

        return ref_pic_list_modification;
    }
}