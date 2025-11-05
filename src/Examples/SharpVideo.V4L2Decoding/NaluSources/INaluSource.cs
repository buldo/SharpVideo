using System.Threading.Channels;
using SharpVideo.H264;

namespace SharpVideo.V4L2Decoding.NaluSources;

/// <summary>
/// Abstraction for providing H.264 NAL units to the decoder.
/// Designed for minimal latency using Channel-based push model.
/// </summary>
public interface INaluSource : IAsyncDisposable
{
    /// <summary>
    /// Channel reader for consuming NAL units as they become available.
    /// Optimized for single-reader scenarios with minimal latency.
    /// </summary>
    ChannelReader<H264Nalu> NaluChannel { get; }

    /// <summary>
    /// Starts providing NAL units to the channel.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops providing NAL units and completes the channel.
    /// </summary>
    Task StopAsync();
}
