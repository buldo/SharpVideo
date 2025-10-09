namespace SharpVideo.H264;

/// <summary>
/// The parsed state of an seq_parameter_set_data() RBSP. Only some select
/// values are stored.
/// Add more as they are actually needed.
/// </summary>
public class SpsDataState
{

#if DEBUG
    //void fdump(FILE* outfp, int indent_level, ParsingOptions parsing_options);
#endif

    public UInt32 profile_idc = 0;
    public UInt32 constraint_set0_flag = 0;
    public UInt32 constraint_set1_flag = 0;
    public UInt32 constraint_set2_flag = 0;
    public UInt32 constraint_set3_flag = 0;
    public UInt32 constraint_set4_flag = 0;
    public UInt32 constraint_set5_flag = 0;
    public UInt32 reserved_zero_2bits = 0;
    public UInt32 level_idc = 0;
    public UInt32 seq_parameter_set_id = 0;
    public UInt32 chroma_format_idc = 0;
    public UInt32 separate_colour_plane_flag = 0;
    public UInt32 bit_depth_luma_minus8 = 0;
    public UInt32 bit_depth_chroma_minus8 = 0;
    public UInt32 qpprime_y_zero_transform_bypass_flag = 0;
    public UInt32 seq_scaling_matrix_present_flag = 0;
    public List<UInt32> seq_scaling_list_present_flag;
    // scaling_list()
    public List<UInt32> ScalingList4x4;
    public List<UInt32> UseDefaultScalingMatrix4x4Flag;
    public List<UInt32> ScalingList8x8;
    public List<UInt32> UseDefaultScalingMatrix8x8Flag;
    public Int32 delta_scale = 0;
    public UInt32 log2_max_frame_num_minus4 = 0;
    public UInt32 pic_order_cnt_type = 0;
    public UInt32 log2_max_pic_order_cnt_lsb_minus4 = 0;
    public UInt32 delta_pic_order_always_zero_flag = 0;
    public Int32 offset_for_non_ref_pic = 0;
    public Int32 offset_for_top_to_bottom_field = 0;
    public UInt32 num_ref_frames_in_pic_order_cnt_cycle = 0;
    public List<Int32> offset_for_ref_frame;
    public UInt32 max_num_ref_frames = 0;
    public UInt32 gaps_in_frame_num_value_allowed_flag = 0;
    public UInt32 pic_width_in_mbs_minus1 = 0;
    public UInt32 pic_height_in_map_units_minus1 = 0;
    public UInt32 frame_mbs_only_flag = 0;
    public UInt32 mb_adaptive_frame_field_flag = 0;
    public UInt32 direct_8x8_inference_flag = 0;
    public UInt32 frame_cropping_flag = 0;
    public UInt32 frame_crop_left_offset = 0;
    public UInt32 frame_crop_right_offset = 0;
    public UInt32 frame_crop_top_offset = 0;
    public UInt32 frame_crop_bottom_offset = 0;
    public UInt32 vui_parameters_present_flag = 0;
    public VuiParametersState vui_parameters;

    // derived values
    public ProfileType profile_type;

    public ProfileType GetProfileType()
    {
        switch (profile_idc)
        {
            case 66:  // Baseline profiles
                if (constraint_set1_flag == 1)
                {
                    return ProfileType.CONSTRAINED_BASELINE;
                }
                return ProfileType.BASELINE;

            case 77:
                return ProfileType.MAIN;

            case 88:
                return ProfileType.EXTENDED;

            case 100:  // High profiles
                if (constraint_set4_flag == 1)
                {
                    return ProfileType.PROGRESSIVE_HIGH;
                }
                if (constraint_set5_flag == 1)
                {
                    return ProfileType.CONSTRAINED_HIGH;
                }
                return ProfileType.HIGH;

            case 110:
                if (constraint_set3_flag == 1)
                {
                    return ProfileType.HIGH_10_INTRA;
                }
                if (constraint_set4_flag == 1)
                {
                    return ProfileType.PROGRESSIVE_HIGH_10;
                }
                return ProfileType.HIGH_10;

            case 122:
                if (constraint_set3_flag == 1)
                {
                    return ProfileType.HIGH_422_INTRA;
                }
                return ProfileType.HIGH_422;

            case 144:
                if (constraint_set3_flag == 1)
                {
                    return ProfileType.HIGH_444_INTRA;
                }
                return ProfileType.HIGH_444;

            case 244:
                if (constraint_set3_flag == 1)
                {
                    return ProfileType.HIGH_444_PRED_INTRA;
                }
                return ProfileType.HIGH_444_PRED;

            case 44:
                return ProfileType.CAVLC_444_INTRA;

            default:
                return ProfileType.UNSPECIFIED;
        }
    }

    public UInt32 getChromaArrayType()
    {
        // Rec. ITU-T H.264 (2012) Page 74, Section 7.4.2.1.1
        // the value of the variable ChromaArrayType is assigned as follows:
        // - If separate_colour_plane_flag is equal to 0, ChromaArrayType is set
        //   equal to chroma_format_idc.
        // - Otherwise (separate_colour_plane_flag is equal to 1), ChromaArrayType
        // is set equal to 0.
        UInt32 ChromaArrayType = 0;
        if (separate_colour_plane_flag == 0)
        {
            ChromaArrayType = chroma_format_idc;
        }
        else
        {
            ChromaArrayType = 0;
        }

        return ChromaArrayType;
    }

    public int getSubWidthC()
    {
        // Table 6-1
        if (chroma_format_idc == 0 && separate_colour_plane_flag == 0)
        {
            // monochrome
            return -1;
        }
        else if (chroma_format_idc == 1 && separate_colour_plane_flag == 0)
        {
            // 4:2:0
            return 2;
        }
        else if (chroma_format_idc == 2 && separate_colour_plane_flag == 0)
        {
            // 4:2:2
            return 2;
        }
        else if (chroma_format_idc == 3 && separate_colour_plane_flag == 0)
        {
            // 4:4:4
            return 1;
        }
        else if (chroma_format_idc == 3 && separate_colour_plane_flag == 1)
        {
            // 4:4:0
            return -1;
        }
        return -1;
    }

    public int getSubHeightC()
    {
        // Table 6-1
        if (chroma_format_idc == 0 && separate_colour_plane_flag == 0)
        {
            // monochrome
            return -1;
        }
        else if (chroma_format_idc == 1 && separate_colour_plane_flag == 0)
        {
            // 4:2:0
            return 2;
        }
        else if (chroma_format_idc == 2 && separate_colour_plane_flag == 0)
        {
            // 4:2:2
            return 1;
        }
        else if (chroma_format_idc == 3 && separate_colour_plane_flag == 0)
        {
            // 4:4:4
            return 1;
        }
        else if (chroma_format_idc == 3 && separate_colour_plane_flag == 1)
        {
            // 4:4:0
            return -1;
        }
        return -1;
    }

    public int getResolution(out int width, out int height)
    {
        var ChromaArrayType = getChromaArrayType();
        int CropUnitX = -1;
        int CropUnitY = -1;
        if (ChromaArrayType == 0)
        {
            // Equations 7-19, 7-20
            CropUnitX = 1;
            CropUnitY = (int)(2 - frame_mbs_only_flag);
        }
        else
        {
            // Equations 7-21, 7-22
            int SubWidthC = getSubWidthC();
            int SubHeightC = getSubHeightC();
            CropUnitX = SubWidthC;
            CropUnitY = (int)(SubHeightC * (2 - frame_mbs_only_flag));
        }

        width = (int)(16 * (pic_width_in_mbs_minus1 + 1));

        height = (int)(16 * (pic_height_in_map_units_minus1 + 1));

        width -= (int)(CropUnitX * frame_crop_left_offset +
                  CropUnitX * frame_crop_right_offset);

        height -= (int)(CropUnitY * frame_crop_top_offset +
                    CropUnitY * frame_crop_bottom_offset);
        return 0;
    }

    public bool scaling_list(
        BitBuffer bit_buffer,
        UInt32 i,
        List<UInt32> scalingList,
        UInt32 sizeOfScalingList,
        List<UInt32> useDefaultScalingMatrixFlag)
    {
        UInt32 lastScale = 8;
        UInt32 nextScale = 8;
        for (UInt32 j = 0; j < sizeOfScalingList; j++)
        {
            if (nextScale != 0)
            {
                // delta_scale  se(v)
                if (!bit_buffer.ReadSignedExponentialGolomb(out delta_scale))
                {
                    return false;
                }
                nextScale = (uint)((lastScale + (delta_scale) + 256) % 256);
                // make sure vector has ith element
                while (useDefaultScalingMatrixFlag.Count <= i)
                {
                    useDefaultScalingMatrixFlag.Add(0);
                }

                useDefaultScalingMatrixFlag[(int)i] = (uint)((j == 0 && nextScale == 0) ? 1 : 0);
            }
            // make sure vector has jth element
            while (scalingList.Count <= j)
            {
                scalingList.Add(0);
            }
            scalingList[(int)j] = (nextScale == 0) ? lastScale : nextScale;
            lastScale = scalingList[(int)j];
        }
        return true;
    }
};