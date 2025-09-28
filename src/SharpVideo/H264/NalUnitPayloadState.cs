namespace SharpVideo.H264;

/// <summary>
/// The parsed state of the NAL Unit Payload.
/// Only some select values are stored.
/// Add more as they are actually needed.
/// </summary>
public class NalUnitPayloadState
{
    public SpsState sps;
    public PpsState pps;
    public SliceLayerWithoutPartitioningRbspState slice_layer_without_partitioning_rbsp;
    public PrefixNalUnitRbspState prefix_nal_unit;
    public SubsetSpsState subset_sps;
    public SliceLayerExtensionRbspState slice_layer_extension_rbsp;
}