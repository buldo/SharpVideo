namespace SharpVideo.H264;

/// <summary>
/// A class for parsing out a SPS VUI data from an H264 NALU.
/// </summary>
public class H264VuiParametersParser
{

    // Section E.2.1: "The value of chroma_sample_loc_type_top_field [...]
    // shall be in the range of 0 to 5, inclusive."
    public const UInt32 kChromaSampleLocTypeTopFieldMin = 0;
    public const UInt32 kChromaSampleLocTypeTopFieldMax = 5;
    // Section E.2.1: "The value of [...] chroma_sample_loc_type_bottom_field
    // shall be in the range of 0 to 5, inclusive."
    public const UInt32 kChromaSampleLocTypeBottomFieldMin = 0;
    public const UInt32 kChromaSampleLocTypeBottomFieldMax = 5;
    // Section E.2.1: "The value of max_bytes_per_pic_denom shall be in the
    // range of 0 to 16, inclusive."
    public const UInt32 kMaxBytesPerPicDenomMin = 0;
    public const UInt32 kMaxBytesPerPicDenomMax = 16;
    // Section E.2.1: "The value of max_bits_per_mb_denom shall be in the
    // range of 0 to 16, inclusive."
    public const UInt32 kMaxBitsPerMbDenomMin = 0;
    public const UInt32 kMaxBitsPerMbDenomMax = 16;
    // Section E.2.1: "The value of log2_max_mv_length_horizontal shall be in
    // the range of 0 to 16, inclusive."
    public const UInt32 kLog2MaxMvLengthHorizontalMin = 0;
    public const UInt32 kLog2MaxMvLengthHorizontalMax = 16;
    // Section E.2.1: "The value of log2_max_mv_length_vertical shall be in
    // the range of 0 to 16, inclusive."
    public const UInt32 kLog2MaxMvLengthVerticalMin = 0;
    public const UInt32 kLog2MaxMvLengthVerticalMax = 16;
    // Section E.2.1: "The value of max_num_reorder_frames shall be in
    // the range of 0 to max_dec_frame_buffering, inclusive."
    // copied from ffmpeg
    public const UInt32 kMaxDpbFrames = 16;
    // The parsed state of the SPS VUI. Only some select values are stored.
    // Add more as they are actually needed.


    /// <summary>
    /// Unpack RBSP and parse VUI Parameters state from the supplied buffer.
    /// </summary>
    public static VuiParametersState ParseVuiParameters(ReadOnlySpan<byte> data)
    {
        var unpacked_buffer = H264Common.UnescapeRbsp(data);
        BitBuffer bit_buffer = new BitBuffer(unpacked_buffer.ToArray());
        return ParseVuiParameters(bit_buffer);
    }

    public static VuiParametersState ParseVuiParameters(BitBuffer bit_buffer)
    {
        // H264 vui_parameters() parser.
        // Section E.1 ("VUI parameters syntax") of the H.264 standard for
        // a complete description.
        var vui = new VuiParametersState();

        // aspect_ratio_info_present_flag  u(1)
        if (!bit_buffer.ReadBits(1, out vui.aspect_ratio_info_present_flag))
        {
            return null;
        }

        if (vui.aspect_ratio_info_present_flag != 0)
        {
            // aspect_ratio_idc  u(8)
            if (!bit_buffer.ReadBits(8, out vui.aspect_ratio_idc))
            {
                return null;
            }
            if (vui.aspect_ratio_idc == (uint)AspectRatioType.AR_EXTENDED_SAR)
            {
                // sar_width  u(16)
                if (!bit_buffer.ReadBits(16, out vui.sar_width))
                {
                    return null;
                }
                // sar_height  u(16)
                if (!bit_buffer.ReadBits(16, out vui.sar_height))
                {
                    return null;
                }
            }
        }

        // overscan_info_present_flag  u(1)
        if (!bit_buffer.ReadBits(1, out vui.overscan_info_present_flag))
        {
            return null;
        }

        if (vui.overscan_info_present_flag != 0)
        {
            // overscan_appropriate_flag  u(1)
            if (!bit_buffer.ReadBits(1, out vui.overscan_appropriate_flag))
            {
                return null;
            }
        }

        // video_signal_type_present_flag  u(1)
        if (!bit_buffer.ReadBits(1, out vui.video_signal_type_present_flag))
        {
            return null;
        }

        if (vui.video_signal_type_present_flag != 0)
        {
            // video_format  u(3)
            if (!bit_buffer.ReadBits(3, out vui.video_format))
            {
                return null;
            }
            // video_full_range_flag  u(1)
            if (!bit_buffer.ReadBits(1, out vui.video_full_range_flag))
            {
                return null;
            }
            // colour_description_present_flag  u(1)
            if (!bit_buffer.ReadBits(1, out vui.colour_description_present_flag))
            {
                return null;
            }
            if (vui.colour_description_present_flag != 0)
            {
                // colour_primaries  u(8)
                if (!bit_buffer.ReadBits(8, out vui.colour_primaries))
                {
                    return null;
                }
                // transfer_characteristics  u(8)
                if (!bit_buffer.ReadBits(8, out vui.transfer_characteristics))
                {
                    return null;
                }
                // matrix_coefficients  u(8)
                if (!bit_buffer.ReadBits(8, out vui.matrix_coefficients))
                {
                    return null;
                }
            }
        }

        // chroma_loc_info_present_flag  u(1)
        if (!bit_buffer.ReadBits(1, out vui.chroma_loc_info_present_flag))
        {
            return null;
        }
        if (vui.chroma_loc_info_present_flag != 0)
        {
            // chroma_sample_loc_type_top_field  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out vui.chroma_sample_loc_type_top_field))
            {
                return null;
            }
            if (vui.chroma_sample_loc_type_top_field < kChromaSampleLocTypeTopFieldMin ||
                vui.chroma_sample_loc_type_top_field > kChromaSampleLocTypeTopFieldMax)
            {
#if DEBUG
                //fprintf(stderr, "invalid chroma_sample_loc_type_top_field: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", vui.chroma_sample_loc_type_top_field, kChromaSampleLocTypeTopFieldMin, kChromaSampleLocTypeTopFieldMax);
#endif  // FPRINT_ERRORS
                return null;
            }

            // chroma_sample_loc_type_bottom_field  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out vui.chroma_sample_loc_type_bottom_field))
            {
                return null;
            }
            if (vui.chroma_sample_loc_type_bottom_field < kChromaSampleLocTypeBottomFieldMin ||
                vui.chroma_sample_loc_type_bottom_field > kChromaSampleLocTypeBottomFieldMax)
            {
#if DEBUG
                //fprintf(stderr, "invalid chroma_sample_loc_type_bottom_field: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", vui.chroma_sample_loc_type_bottom_field, kChromaSampleLocTypeBottomFieldMin, kChromaSampleLocTypeBottomFieldMax);
#endif  // FPRINT_ERRORS
                return null;
            }
        }

        // timing_info_present_flag  u(1)
        if (!bit_buffer.ReadBits(1, out vui.timing_info_present_flag))
        {
            return null;
        }

        if (vui.timing_info_present_flag != 0)
        {
            // num_units_in_tick  u(32)
            if (!bit_buffer.ReadBits(32, out vui.num_units_in_tick))
            {
                return null;
            }
            // time_scale  u(32)
            if (!bit_buffer.ReadBits(32, out vui.time_scale))
            {
                return null;
            }
            // fixed_frame_rate_flag  u(1)
            if (!bit_buffer.ReadBits(1, out vui.fixed_frame_rate_flag))
            {
                return null;
            }
        }

        // nal_hrd_parameters_present_flag  u(1)
        if (!bit_buffer.ReadBits(1, out vui.nal_hrd_parameters_present_flag))
        {
            return null;
        }

        if (vui.nal_hrd_parameters_present_flag != 0)
        {
            // hrd_parameters()
            vui.nal_hrd_parameters = H264HrdParametersParser.ParseHrdParameters(bit_buffer);
            if (vui.nal_hrd_parameters == null)
            {
                return null;
            }
        }

        // vcl_hrd_parameters_present_flag  u(1)
        if (!bit_buffer.ReadBits(1, out vui.vcl_hrd_parameters_present_flag))
        {
            return null;
        }

        if (vui.vcl_hrd_parameters_present_flag != 0)
        {
            // hrd_parameters()
            vui.vcl_hrd_parameters = H264HrdParametersParser.ParseHrdParameters(bit_buffer);
            if (vui.vcl_hrd_parameters == null)
            {
                return null;
            }
        }

        if (vui.nal_hrd_parameters_present_flag != 0 ||
            vui.vcl_hrd_parameters_present_flag != 0)
        {
            // low_delay_hrd_flag  u(1)
            if (!bit_buffer.ReadBits(1, out vui.low_delay_hrd_flag))
            {
                return null;
            }
        }

        // pic_struct_present_flag  u(1)
        if (!bit_buffer.ReadBits(1, out vui.pic_struct_present_flag))
        {
            return null;
        }

        // bitstream_restriction_flag  u(1)
        if (!bit_buffer.ReadBits(1, out vui.bitstream_restriction_flag))
        {
            return null;
        }

        if (vui.bitstream_restriction_flag != 0)
        {
            // motion_vectors_over_pic_boundaries_flag  u(1)
            if (!bit_buffer.ReadBits(1, out vui.motion_vectors_over_pic_boundaries_flag))
            {
                return null;
            }
            // max_bytes_per_pic_denom  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out vui.max_bytes_per_pic_denom))
            {
                return null;
            }
            if (vui.max_bytes_per_pic_denom < kMaxBytesPerPicDenomMin ||
                vui.max_bytes_per_pic_denom > kMaxBytesPerPicDenomMax)
            {
#if DEBUG
                //fprintf(stderr, "invalid max_bytes_per_pic_denom: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", vui.max_bytes_per_pic_denom, kMaxBytesPerPicDenomMin, kMaxBytesPerPicDenomMax);
#endif  // FPRINT_ERRORS
                return null;
            }

            // max_bits_per_mb_denom  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out vui.max_bits_per_mb_denom))
            {
                return null;
            }
            if (vui.max_bits_per_mb_denom < kMaxBitsPerMbDenomMin ||
                vui.max_bits_per_mb_denom > kMaxBitsPerMbDenomMax)
            {
#if DEBUG
                //fprintf(stderr, "invalid max_bits_per_mb_denom: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", vui.max_bits_per_mb_denom, kMaxBitsPerMbDenomMin, kMaxBitsPerMbDenomMax);
#endif  // FPRINT_ERRORS
                return null;
            }

            // log2_max_mv_length_horizontal  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out vui.log2_max_mv_length_horizontal))
            {
                return null;
            }
            if (vui.log2_max_mv_length_horizontal < kLog2MaxMvLengthHorizontalMin ||
                vui.log2_max_mv_length_horizontal > kLog2MaxMvLengthHorizontalMax)
            {
#if DEBUG
                //fprintf(stderr, "invalid log2_max_mv_length_horizontal: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", vui.log2_max_mv_length_horizontal, kLog2MaxMvLengthHorizontalMin, kLog2MaxMvLengthHorizontalMax);
#endif  // FPRINT_ERRORS
                return null;
            }

            // log2_max_mv_length_vertical  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out vui.log2_max_mv_length_vertical))
            {
                return null;
            }
            if (vui.log2_max_mv_length_vertical < kLog2MaxMvLengthVerticalMin ||
                vui.log2_max_mv_length_vertical > kLog2MaxMvLengthVerticalMax)
            {
#if DEBUG
                //fprintf(stderr,"invalid log2_max_mv_length_vertical: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", vui.log2_max_mv_length_vertical, kLog2MaxMvLengthVerticalMin, kLog2MaxMvLengthVerticalMax);
#endif  // FPRINT_ERRORS
                return null;
            }

            // max_num_reorder_frames  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out vui.max_num_reorder_frames))
            {
                return null;
            }
            if (vui.max_num_reorder_frames > kMaxDpbFrames)
            {
#if DEBUG
                //fprintf(stderr, "invalid max_num_reorder_frames: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", vui.max_num_reorder_frames, 0, kMaxDpbFrames);
#endif  // FPRINT_ERRORS
                return null;
            }

            // max_dec_frame_buffering  ue(v)
            if (!bit_buffer.ReadExponentialGolomb(out vui.max_dec_frame_buffering))
            {
                return null;
            }
            // Section E.2.1: "The value of max_dec_frame_buffering shall be greater
            // than or equal to max_num_ref_frames."
            // copied from ffmpeg
            if (vui.max_dec_frame_buffering > kMaxDpbFrames)
            {
#if DEBUG
                //fprintf(stderr, "invalid max_dec_frame_buffering: %" PRIu32" not in range ""[%" PRIu32 ", %" PRIu32 "]\n", vui.max_dec_frame_buffering, 0, kMaxDpbFrames);
#endif  // FPRINT_ERRORS
                return null;
            }
        }

        return vui;
    }
}