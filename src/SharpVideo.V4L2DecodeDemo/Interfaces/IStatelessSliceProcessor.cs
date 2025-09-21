namespace SharpVideo.V4L2DecodeDemo.Interfaces;

/// <summary>
/// Interface for processing H.264 slice data for stateless decoders
/// </summary>
public interface IStatelessSliceProcessor
{
    /// <summary>
    /// Process slice data from video file
    /// </summary>
    Task ProcessVideoFileAsync(int deviceFd, string filePath, Action<double> progressCallback);

    /// <summary>
    /// Process video file NALU by NALU with frame callbacks
    /// </summary>
    Task ProcessVideoFileNaluByNaluAsync(int deviceFd, string filePath, Action<object> frameCallback, Action<double> progressCallback);
}