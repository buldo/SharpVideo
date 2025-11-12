using System.Collections.Concurrent;
using SharpVideo.H264;

namespace SharpVideo.V4L2Decoding.NaluSources;

/// <summary>
/// Abstraction for providing H.264 NAL units to the decoder.
/// Designed for minimal latency using BlockingCollection for synchronous consumption.
/// </summary>
public interface INaluSource : IAsyncDisposable
{
    /// <summary>
    /// Blocking collection for consuming NAL units as they become available.
    /// Optimized for minimal latency with synchronous Take() operations.
    /// </summary>
    BlockingCollection<H264Nalu> NaluQueue { get; }

    /// <summary>
    /// Starts providing NAL units to the queue.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops providing NAL units and marks collection as complete.
    /// </summary>
    Task StopAsync();
}
