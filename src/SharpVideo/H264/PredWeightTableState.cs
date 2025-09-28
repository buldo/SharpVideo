namespace SharpVideo.H264;

/// <summary>
/// The parsed state of the PredWeightTable.
/// </summary>
public class PredWeightTableState
{
    // input parameters
    public uint32_t chroma_array_type = 0;
    public uint32_t slice_type = 0;
    public uint32_t num_ref_idx_l0_active_minus1 = 0;
    public uint32_t num_ref_idx_l1_active_minus1 = 0;

    // contents
    public uint32_t luma_log2_weight_denom = 0;
    public uint32_t chroma_log2_weight_denom = 0;
    public List<uint32_t> luma_weight_l0_flag;
    public List<uint32_t> luma_weight_l0;
    public List<uint32_t> luma_offset_l0;
    public List<uint32_t> chroma_weight_l0_flag;
    public List<List<uint32_t>> chroma_weight_l0;
    public List<List<uint32_t>> chroma_offset_l0;
    public List<uint32_t> luma_weight_l1_flag;
    public List<uint32_t> luma_weight_l1;
    public List<uint32_t> luma_offset_l1;
    public List<uint32_t> chroma_weight_l1_flag;
    public List<List<uint32_t>> chroma_weight_l1;
    public List<List<uint32_t>> chroma_offset_l1;
};