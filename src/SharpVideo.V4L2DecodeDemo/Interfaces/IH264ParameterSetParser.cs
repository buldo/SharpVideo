using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2DecodeDemo.Interfaces;

/// <summary>
/// Interface for parsing H.264 parameter sets (SPS/PPS) and converting them to V4L2 control structures
/// </summary>
public interface IH264ParameterSetParser
{
    /// <summary>
    /// Extract SPS from H.264 bitstream file
    /// </summary>
    Task<V4L2CtrlH264Sps?> ExtractSpsAsync(string filePath);

    /// <summary>
    /// Extract PPS from H.264 bitstream file
    /// </summary>
    Task<V4L2CtrlH264Pps?> ExtractPpsAsync(string filePath);

    /// <summary>
    /// Parse slice header to create V4L2 slice parameters (legacy method)
    /// </summary>
    V4L2CtrlH264SliceParams ParseSliceHeaderToControl(byte[] sliceData, uint frameNum);

    /// <summary>
    /// Get NALU header position in data
    /// </summary>
    int GetNaluHeaderPosition(byte[] data, bool skipStartCode = true);
}