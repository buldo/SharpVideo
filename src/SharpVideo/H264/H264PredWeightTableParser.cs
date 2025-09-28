namespace SharpVideo.H264;

/// <summary>
/// A class for parsing out a prediction weight table
/// (`pred_weight_table()`, as defined in Section 7.3.3.2 of the 2012
/// standard) from an H264 NALU.
/// </summary>
class H264PredWeightTableParser
{

    // Section 7.4.3.2: "The value of luma_log2_weight_denom shall be in the
    // range of 0 to 7, inclusive."
    const uint32_t kLumaLog2WeightDenomMin = 0;
    const uint32_t kLumaLog2WeightDenomMax = 7;

    // Section 7.4.3.2: "The value of chroma_log2_weight_denom shall be in the
    // range of 0 to 7, inclusive."
    const uint32_t kChromaLog2WeightDenomMin = 0;
    const uint32_t kChromaLog2WeightDenomMax = 7;

    /// <summary>
    /// Unpack RBSP and parse PredWeightTable state from the supplied buffer.
    /// </summary>
    static PredWeightTableState? ParsePredWeightTable(ReadOnlySpan<byte> data, uint32_t chroma_array_type,
        uint32_t slice_type, uint32_t num_ref_idx_l0_active_minus1, uint32_t num_ref_idx_l1_active_minus1)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new(unpacked_buffer.ToArray());
        return ParsePredWeightTable(
            bit_buffer,
            chroma_array_type,
            slice_type,
            num_ref_idx_l0_active_minus1,
            num_ref_idx_l1_active_minus1);
    }

    public static PredWeightTableState? ParsePredWeightTable(
        BitBuffer bit_buffer,
        uint32_t chroma_array_type,
        uint32_t slice_type,
        uint32_t num_ref_idx_l0_active_minus1,
        uint32_t num_ref_idx_l1_active_minus1)
    {
        uint32_t bits_tmp;
        int32_t sgolomb_tmp;

        // H264 pred_weight_table() NAL Unit.
        // Section 7.3.3.2 ("Prediction weight table syntax") of the
        // H.264 standard for a complete description.
        var pred_weight_table = new PredWeightTableState();

        // store input values
        pred_weight_table.chroma_array_type = chroma_array_type;
        pred_weight_table.slice_type = slice_type;
        pred_weight_table.num_ref_idx_l0_active_minus1 = num_ref_idx_l0_active_minus1;
        pred_weight_table.num_ref_idx_l1_active_minus1 = num_ref_idx_l1_active_minus1;

        // luma_log2_weight_denom  ue(v)
        if (!bit_buffer.ReadExponentialGolomb(out pred_weight_table.luma_log2_weight_denom))
        {
            return null;
        }

        if (pred_weight_table.luma_log2_weight_denom < kLumaLog2WeightDenomMin ||
            pred_weight_table.luma_log2_weight_denom > kLumaLog2WeightDenomMax)
        {
#if DEBUG
            //fprintf(stderr, "invalid luma_log2_weight_denom: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", pred_weight_table.luma_log2_weight_denom, kLumaLog2WeightDenomMin, kLumaLog2WeightDenomMax);
#endif // FPRINT_ERRORS
            return null;
        }

        if (pred_weight_table.chroma_array_type != 0)
        {
            // chroma_log2_weight_denom  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out pred_weight_table.chroma_log2_weight_denom))
            {
                return null;
            }

            if (pred_weight_table.chroma_log2_weight_denom < kChromaLog2WeightDenomMin ||
                pred_weight_table.chroma_log2_weight_denom > kChromaLog2WeightDenomMax)
            {
#if DEBUG
                //fprintf(stderr, "invalid chroma_log2_weight_denom: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", pred_weight_table.chroma_log2_weight_denom, kChromaLog2WeightDenomMin, kChromaLog2WeightDenomMax);
#endif // FPRINT_ERRORS
                return null;
            }
        }

        for (int i = 0; i <= pred_weight_table.num_ref_idx_l0_active_minus1; ++i)
        {
            // luma_weight_l0_flag[i]  u(1)
            if (!bit_buffer.ReadBits(1, out bits_tmp))
            {
                return null;
            }

            pred_weight_table.luma_weight_l0_flag.Add(bits_tmp);

            if (pred_weight_table.luma_weight_l0_flag[i] != 0)
            {
                // luma_weight_l0[i]  se(v)
                if (!bit_buffer.ReadSignedExponentialGolomb(out sgolomb_tmp))
                {
                    return null;
                }

                pred_weight_table.luma_weight_l0.Add((uint32_t)sgolomb_tmp);

                // luma_offset_l0[i]  se(v)
                if (!bit_buffer.ReadSignedExponentialGolomb(out sgolomb_tmp))
                {
                    return null;
                }

                pred_weight_table.luma_offset_l0.Add((uint32_t)sgolomb_tmp);
            }

            if (pred_weight_table.chroma_array_type != 0)
            {
                // chroma_weight_l0_flag[i]  u(1)
                if (!bit_buffer.ReadBits(1, out bits_tmp))
                {
                    return null;
                }

                pred_weight_table.chroma_weight_l0_flag.Add(bits_tmp);

                if (pred_weight_table.chroma_weight_l0_flag[i] != 0)
                {
                    pred_weight_table.chroma_weight_l0.Add(new());
                    pred_weight_table.chroma_offset_l0.Add(new());
                    for (uint32_t j = 0; j < 2; ++j)
                    {
                        // chroma_weight_l0[i][j]  se(v)
                        if (!bit_buffer.ReadSignedExponentialGolomb(out sgolomb_tmp))
                        {
                            return null;
                        }

                        pred_weight_table.chroma_weight_l0.Last().Add((uint32_t)sgolomb_tmp);

                        // chroma_offset_l0[i][j]  se(v)
                        if (!bit_buffer.ReadSignedExponentialGolomb(out sgolomb_tmp))
                        {
                            return null;
                        }

                        pred_weight_table.chroma_offset_l0.Last().Add((uint32_t)sgolomb_tmp);
                    }
                }
            }
        }

        if ((slice_type == (uint)SliceType.B) || (slice_type == (uint)SliceType.B_ALL))
        {
            // slice_type == B
            for (int i = 0; i <= pred_weight_table.num_ref_idx_l1_active_minus1; ++i)
            {
                // luma_weight_l1_flag[i]  u(1)
                if (!bit_buffer.ReadBits(1, out bits_tmp))
                {
                    return null;
                }

                pred_weight_table.luma_weight_l1_flag.Add(bits_tmp);

                if (pred_weight_table.luma_weight_l1_flag[i] != 0)
                {
                    // luma_weight_l1[i]  se(v)
                    if (!bit_buffer.ReadSignedExponentialGolomb(out sgolomb_tmp))
                    {
                        return null;
                    }

                    pred_weight_table.luma_weight_l1.Add((uint32_t)sgolomb_tmp);

                    // luma_offset_l1[i]  se(v)
                    if (!bit_buffer.ReadSignedExponentialGolomb(out sgolomb_tmp))
                    {
                        return null;
                    }

                    pred_weight_table.luma_offset_l1.Add((uint32_t)sgolomb_tmp);
                }

                if (pred_weight_table.chroma_array_type != 0)
                {
                    // chroma_weight_l1_flag[i]  u(1)
                    if (!bit_buffer.ReadBits(1, out bits_tmp))
                    {
                        return null;
                    }

                    pred_weight_table.chroma_weight_l1_flag.Add(bits_tmp);

                    if (pred_weight_table.chroma_weight_l1_flag[i] != 0)
                    {
                        // chroma_weight_l1[i][j]  se(v)
                        pred_weight_table.chroma_weight_l1.Add(new());

                        // chroma_offset_l1[i][j]  se(v)
                        pred_weight_table.chroma_offset_l1.Add(new());
                        for (uint32_t j = 0; j < 2; ++j)
                        {
                            if (!bit_buffer.ReadSignedExponentialGolomb(out sgolomb_tmp))
                            {
                                return null;
                            }

                            pred_weight_table.chroma_weight_l1.Last().Add((uint32_t)sgolomb_tmp);

                            if (!bit_buffer.ReadSignedExponentialGolomb(out sgolomb_tmp))
                            {
                                return null;
                            }

                            pred_weight_table.chroma_offset_l1.Last().Add((uint32_t)sgolomb_tmp);
                        }
                    }
                }
            }
        }

        return pred_weight_table;
    }
}