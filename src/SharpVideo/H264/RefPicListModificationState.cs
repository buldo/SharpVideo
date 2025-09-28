namespace SharpVideo.H264;

/// <summary>
/// The parsed state of the RefPicListModification.
/// </summary>
public class RefPicListModificationState
{

    // input parameters
    public uint32_t slice_type = 0;

    // contents
    public uint32_t ref_pic_list_modification_flag_l0 = 0;
    public List<uint32_t> modification_of_pic_nums_idc = new();
    public List<uint32_t> abs_diff_pic_num_minus1 = new();
    public List<uint32_t> long_term_pic_num = new();
    public uint32_t ref_pic_list_modification_flag_l1 = 0;
}