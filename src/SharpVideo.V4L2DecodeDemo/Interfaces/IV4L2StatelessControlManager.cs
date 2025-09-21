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
    /// Set slice parameters controls (legacy method)
    /// </summary>
    Task SetSliceParamsControlsAsync(byte[] sliceData, byte sliceType, CancellationToken cancellationToken);

    /// <summary>
    /// Configure stateless controls (legacy method)
    /// </summary>
    Task<bool> ConfigureStatelessControlsAsync(CancellationToken cancellationToken);
}