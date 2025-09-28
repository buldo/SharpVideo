namespace SharpVideo.H264;

/// <summary>
/// The parsed state of the NAL Unit Header SVC Extension.
/// </summary>
public class NalUnitHeaderSvcExtensionState
{

    public uint32_t idr_flag = 0;
    public uint32_t priority_id = 0;
    public uint32_t no_inter_layer_pred_flag = 0;
    public uint32_t dependency_id = 0;
    public uint32_t quality_id = 0;
    public uint32_t temporal_id = 0;
    public uint32_t use_ref_base_pic_flag = 0;
    public uint32_t discardable_flag = 0;
    public uint32_t output_flag = 0;
    public uint32_t reserved_three_2bits = 0;
}