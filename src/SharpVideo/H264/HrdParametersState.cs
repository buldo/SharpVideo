namespace SharpVideo.H264;

public class HrdParametersState
{

#if DEBUG
//void fdump(FILE* outfp, int indent_level) const;
#endif // FDUMP_DEFINE

// contents
    public UInt32 cpb_cnt_minus1 = 0;
    public UInt32 bit_rate_scale = 0;
    public UInt32 cpb_size_scale = 0;
    public List<UInt32> bit_rate_value_minus1 = new();
    public List<UInt32> cpb_size_value_minus1 = new();
    public List<UInt32> cbr_flag = new();
    public UInt32 initial_cpb_removal_delay_length_minus1 = 0;
    public UInt32 cpb_removal_delay_length_minus1 = 0;
    public UInt32 dpb_output_delay_length_minus1 = 0;
    public UInt32 time_offset_length = 0;
}