using System.Collections.Concurrent;
using SharpVideo.H264;

namespace SharpVideo.FfmpegDemo.NaluSources;

/// <summary>
/// Interface for providing H.264 NAL units to the decoder
/// </summary>
public interface INaluSource : IAsyncDisposable
{
    /// <summary>
    /// Queue of NAL units to be consumed by the decoder
    /// </summary>
    BlockingCollection<H264Nalu> NaluQueue { get; }

    /// <summary>
    /// Start providing NAL units
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop providing NAL units
    /// </summary>
    Task StopAsync();
}
