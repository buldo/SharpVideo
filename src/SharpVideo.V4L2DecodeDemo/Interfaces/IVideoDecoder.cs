using SharpVideo.V4L2DecodeDemo.Models;

namespace SharpVideo.V4L2DecodeDemo.Interfaces;

/// <summary>
/// Interface for video decoders that support streaming operations
/// </summary>
public interface IVideoDecoder : IDisposable
{
    /// <summary>
    /// Decodes a video file asynchronously
    /// </summary>
    /// <param name="filePath">Path to the video file to decode</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Task representing the decode operation</returns>
    Task DecodeFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a frame is decoded
    /// </summary>
    event EventHandler<FrameDecodedEventArgs>? FrameDecoded;

    /// <summary>
    /// Event raised when decoding progress changes
    /// </summary>
    event EventHandler<DecodingProgressEventArgs>? ProgressChanged;
}