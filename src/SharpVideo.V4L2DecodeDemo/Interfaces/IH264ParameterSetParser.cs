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
    /// Parse SPS NALU data into V4L2 control structure
    /// </summary>
    V4L2CtrlH264Sps ParseSps(ReadOnlySpan<byte> spsData);
    
    /// <summary>
    /// Parse PPS NALU data into V4L2 control structure
    /// </summary>
    V4L2CtrlH264Pps ParsePps(ReadOnlySpan<byte> ppsData);
    
    /// <summary>
    /// Parse slice header to create V4L2 slice parameters
    /// </summary>
    V4L2CtrlH264SliceParams ParseSliceHeader(ReadOnlySpan<byte> sliceData, V4L2CtrlH264Sps sps, V4L2CtrlH264Pps pps);
}