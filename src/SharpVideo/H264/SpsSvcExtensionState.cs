/// <summary>
/// // The parsed state of the SPS SVC extension. Only some select values are
/// stored.
/// Add more as they are actually needed.
/// </summary>
public class SpsSvcExtensionState
{

    // input parameters
    public uint32_t ChromaArrayType = 0;

    // contents
    public uint32_t inter_layer_deblocking_filter_control_present_flag = 0;
    public uint32_t extended_spatial_scalability_idc = 0;
    public uint32_t chroma_phase_x_plus1_flag = 0;
    public uint32_t chroma_phase_y_plus1 = 0;
    public uint32_t seq_ref_layer_chroma_phase_x_plus1_flag = 0;
    public uint32_t seq_ref_layer_chroma_phase_y_plus1 = 0;
    public int32_t seq_scaled_ref_layer_left_offset = 0;
    public int32_t seq_scaled_ref_layer_top_offset = 0;
    public int32_t seq_scaled_ref_layer_right_offset = 0;
    public int32_t seq_scaled_ref_layer_bottom_offset = 0;
    public uint32_t seq_tcoeff_level_prediction_flag = 0;
    public uint32_t adaptive_tcoeff_level_prediction_flag = 0;
    public uint32_t slice_header_restriction_flag = 0;
}