namespace SharpVideo.H264;

/// <summary>
/// The parsed state of the slice. Only some select values are stored.
/// Add more as they are actually needed.
/// </summary>
public class SliceLayerWithoutPartitioningRbspState
{
    // input parameters
    public uint32_t nal_ref_idc = 0;
    public uint32_t nal_unit_type = 0;

    // contents
    public SliceHeaderState slice_header;

    // slice_data()
    // rbsp_slice_trailing_bits()
};