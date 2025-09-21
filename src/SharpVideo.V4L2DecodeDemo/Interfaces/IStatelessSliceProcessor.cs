namespace SharpVideo.V4L2DecodeDemo.Interfaces;

/// <summary>
/// Interface for processing H.264 slice data for stateless decoders
/// </summary>
public interface IStatelessSliceProcessor
{
    /// <summary>
    /// Extract only slice data (remove start codes if needed)
    /// </summary>
    byte[] ExtractSliceDataOnly(byte[] naluData, bool useStartCodes);

    /// <summary>
    /// Queue stateless slice data with proper control setup
    /// </summary>
    Task QueueStatelessSliceDataAsync(byte[] sliceData, byte naluType, uint bufferIndex, CancellationToken cancellationToken);

    /// <summary>
    /// Process slice data from video file
    /// </summary>
    Task ProcessVideoFileAsync(int deviceFd, string filePath, Action<double> progressCallback);

    /// <summary>
    /// Process video file NALU by NALU with frame callbacks
    /// </summary>
    Task ProcessVideoFileNaluByNaluAsync(int deviceFd, string filePath, Action<object> frameCallback, Action<double> progressCallback);

    /// <summary>
    /// Queue slice data to OUTPUT buffer
    /// </summary>
    Task QueueSliceDataAsync(int deviceFd, ReadOnlyMemory<byte> sliceData);

    /// <summary>
    /// Dequeue and return decoded frame from CAPTURE buffer
    /// </summary>
    Task<object?> DequeueFrameAsync(int deviceFd);
}