namespace SharpVideo.H264;

/// <summary>
/// The parsed state of the SPS. Only some select values are stored.
/// Add more as they are actually needed.
/// </summary>
public class SubsetSpsState
{
    public SpsDataState seq_parameter_set_data;

    public SpsSvcExtensionState seq_parameter_set_svc_extension;
    public uint32_t svc_vui_parameters_present_flag = 0;
    // TODO(chema): svc_vui_parameters_extension()
    public uint32_t bit_equal_to_one = 0;
    // TODO(chema): seq_parameter_set_mvc_extension()
    public uint32_t mvc_vui_parameters_present_flag = 0;
    // TODO(chema): mvc_vui_parameters_extension()
    // TODO(chema): seq_parameter_set_mvcd_extension()
    // TODO(chema): seq_parameter_set_3davc_extension()
    public uint32_t additional_extension2_flag = 0;
    public uint32_t additional_extension2_data_flag = 0;
};