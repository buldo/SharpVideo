using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;

namespace SharpVideo.Decoding;

/// <summary>
/// Base class for video decoding.
/// </summary>
/// <remarks>
/// <para>
/// Decoder has a simple synchronous input API and manages output frames:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Input:</b> Call <see cref="Decode"/> to decode a NALU. The decoder manages input buffers internally.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Output:</b> Call <see cref="WaitForDecodedFrames"/> to get decoded frames.
///       After displaying, return frames via <see cref="ReuseDecodedFrame"/>.
///     </description>
///   </item>
/// </list>
/// </remarks>
/// <typeparam name="TInputBuffer">Type of internal encoded data buffer.</typeparam>
/// <typeparam name="TOutputBuffer">Type of decoded frame buffer.</typeparam>
public abstract class BaseDecoder<TInputBuffer, TOutputBuffer> : IDisposable, IDecoder
    where TInputBuffer : UniversalEncodedBuffer
{
    private readonly ILogger _logger;

    private readonly BlockingCollection<TInputBuffer> _freeEncodedBuffers = new();
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
    /// Gets a free encoded buffer from the internal pool.
    /// Blocks until a buffer is available.
    /// </summary>
    /// <returns>A free buffer for encoding data.</returns>
    protected TInputBuffer GetFreeEncodedBuffer()
    {
        return _freeEncodedBuffers.Take();
    }

    /// <summary>
    /// Tries to get a free encoded buffer from the internal pool.
    /// </summary>
    /// <param name="buffer">The buffer if available.</param>
    /// <returns>True if a buffer was available, false otherwise.</returns>
    protected bool TryGetFreeEncodedBuffer(out TInputBuffer? buffer)
    {
        return _freeEncodedBuffers.TryTake(out buffer);
    }

    /// <summary>
    /// Returns an encoded buffer to the free pool for reuse.
    /// </summary>
    /// <param name="encodedBuffer">The buffer to return.</param>
    protected void ReturnEncodedBuffer(TInputBuffer encodedBuffer)
    {
        _freeEncodedBuffers.Add(encodedBuffer);
    }

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
            _freeEncodedBuffers.Dispose();
            _decodedFramesOutput.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

public interface IDecoder
{
}