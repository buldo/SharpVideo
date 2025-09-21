using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2DecodeDemo.Interfaces;

/// <summary>
/// Interface for managing V4L2 stateless decoder extended controls
/// </summary>
public interface IV4L2StatelessControlManager
{
    /// <summary>
    /// Set SPS and PPS parameter sets via V4L2 extended controls
    /// </summary>
    Task SetParameterSetsAsync(int deviceFd, V4L2CtrlH264Sps sps, V4L2CtrlH264Pps pps);

    /// <summary>
    /// Set decode parameters for a frame
    /// </summary>
    Task SetDecodeParametersAsync(int deviceFd, V4L2CtrlH264DecodeParams decodeParams, V4L2CtrlH264SliceParams[] sliceParams);

    /// <summary>
    /// Configure stateless decoder mode and settings
    /// </summary>
    Task ConfigureStatelessModeAsync(int deviceFd);
}