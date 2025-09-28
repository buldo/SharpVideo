namespace SharpVideo.H264;

/// <summary>
/// The parsed state of the DecRefPicMarking.
/// </summary>
public class DecRefPicMarkingState
{

    // input parameters
    public uint32_t nal_unit_type = 0;

    // contents
    public uint32_t no_output_of_prior_pics_flag = 0;
    public uint32_t long_term_reference_flag = 0;
    public uint32_t adaptive_ref_pic_marking_mode_flag = 0;
    public List<uint32_t> memory_management_control_operation;
    public List<uint32_t> difference_of_pic_nums_minus1;
    public List<uint32_t> long_term_pic_num;
    public List<uint32_t> long_term_frame_idx;
    public List<uint32_t> max_long_term_frame_idx_plus1;
}