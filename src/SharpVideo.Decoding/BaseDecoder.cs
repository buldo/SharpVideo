using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;

namespace SharpVideo.Decoding;

/// <summary>
/// Base class for video decoding.
/// </summary>
/// <remarks>
/// <para>
/// Decoder lifecycle:
/// </para>
/// <list type="number">
///   <item>
///     <description>Create decoder instance via factory method.</description>
///   </item>
///   <item>
///     <description>Call <see cref="Initialize"/> to prepare the decoder for use.</description>
///   </item>
///   <item>
///     <description>Call <see cref="Decode"/> to decode NALUs. The decoder manages input buffers internally.</description>
///   </item>
///   <item>
///     <description>Call <see cref="WaitForDecodedFrames"/> to get decoded frames.
///       After displaying, return frames via <see cref="ReuseDecodedFrame"/>.</description>
///   </item>
///   <item>
///     <description>Dispose the decoder when done.</description>
///   </item>
/// </list>
/// </remarks>
/// <typeparam name="TOutputBuffer">Type of decoded frame buffer.</typeparam>
public abstract class BaseDecoder<TOutputBuffer> : IDisposable, IDecoder
{
    private readonly ILogger _logger;

    private readonly BlockingCollection<TOutputBuffer> _decodedFramesOutput = new();

    protected BaseDecoder(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the logger instance.
    /// </summary>
    protected ILogger Logger => _logger;

    /// <summary>
    /// Initializes the decoder. Must be called before <see cref="Decode"/>.
    /// </summary>
    /// <remarks>
    /// This method prepares the decoder for use, allocating necessary resources.
    /// Some decoders may require hardware initialization or device setup.
    /// </remarks>
    public virtual void Initialize()
    {
        // Default implementation does nothing.
        // Derived classes can override to perform initialization.
    }

    /// <summary>
    /// Decodes a single NALU synchronously.
    /// </summary>
    /// <param name="nalu">The NALU data including start code (Annex-B format).</param>
    /// <remarks>
    /// This method may block if internal buffers are exhausted.
    /// Decoded frames are available via <see cref="WaitForDecodedFrames"/>.
    /// </remarks>
    public abstract void Decode(ReadOnlySpan<byte> nalu);

    /// <summary>
    /// Blocking method that waits for decoded frames.
    /// </summary>
    /// <returns>Frame with decoded data.</returns>
    public TOutputBuffer WaitForDecodedFrames()
    {
        return _decodedFramesOutput.Take();
    }

    /// <summary>
    /// Non-blocking attempt to get a decoded frame.
    /// </summary>
    /// <param name="frame">The decoded frame if available.</param>
    /// <returns>True if a frame was available, false otherwise.</returns>
    public bool TryTakeDecodedFrame(out TOutputBuffer? frame)
    {
        return _decodedFramesOutput.TryTake(out frame);
    }

    /// <summary>
    /// Returns a displayed frame for reuse by the decoder.
    /// </summary>
    /// <param name="decodedFrame">The frame to return.</param>
    public abstract void ReuseDecodedFrame(TOutputBuffer decodedFrame);

    /// <summary>
    /// Gets the pixel format of decoded frames output by this decoder.
    /// This is known at construction time and does not change.
    /// </summary>
    public abstract PixelFormat OutputPixelFormat { get; }

    /// <summary>
    /// Flushes any remaining frames from the decoder.
    /// Called during disposal.
    /// </summary>
    protected abstract void FlushDecoder();

    /// <summary>
    /// Adds a decoded frame to the output queue.
    /// </summary>
    /// <param name="decodedFrame">The decoded frame.</param>
    protected void AddDecodedFrameToOutput(TOutputBuffer decodedFrame)
    {
        _decodedFramesOutput.Add(decodedFrame);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            FlushDecoder();
            _decodedFramesOutput.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Interface for video decoders.
/// </summary>
public interface IDecoder : IDisposable
{
    /// <summary>
    /// Initializes the decoder. Must be called before <see cref="Decode"/>.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Decodes a single NALU synchronously.
    /// </summary>
    /// <param name="nalu">The NALU data including start code (Annex-B format).</param>
    void Decode(ReadOnlySpan<byte> nalu);
}