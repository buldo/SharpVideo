using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// Prediction weight factor for a reference frame.
/// Used in weighted prediction for P and B slices.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SupportedOSPlatform("linux")]
public struct V4L2H264WeightFactors
{
    /// <summary>
    /// Luma weight values for up to 32 references.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public short[] LumaWeight;

    /// <summary>
    /// Luma offset values for up to 32 references.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public short[] LumaOffset;

    /// <summary>
    /// Chroma weight values for up to 32 references, 2 chroma components each.
    /// Layout: [ref_idx][chroma_component]
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32 * 2)]
    public short[] ChromaWeight;

    /// <summary>
    /// Chroma offset values for up to 32 references, 2 chroma components each.
    /// Layout: [ref_idx][chroma_component]
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32 * 2)]
    public short[] ChromaOffset;
}

/// <summary>
/// Stateless H.264 prediction weights structure.
/// Used for weighted prediction in P and B slices.
/// Matches struct v4l2_ctrl_h264_pred_weights from Linux v4l2-controls.h.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SupportedOSPlatform("linux")]
public struct V4L2CtrlH264PredWeights
{
    /// <summary>
    /// Log2 of the luma weight denominator (0-7).
    /// </summary>
    public ushort LumaLog2WeightDenom;

    /// <summary>
    /// Log2 of the chroma weight denominator (0-7).
    /// </summary>
    public ushort ChromaLog2WeightDenom;

    /// <summary>
    /// Weight factors for L0 (index 0) and L1 (index 1) reference lists.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public V4L2H264WeightFactors[] WeightFactors;
}
