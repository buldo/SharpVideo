using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;

namespace SharpVideo.Decoding;

/// <summary>
/// Base class for decoding
/// </summary>
/// <remarks>
/// How it works:
/// Decoded have 2 inputs and 2 outputs:
/// * IO for buffers with encoded data
///   * Input for buffers with encoded data. For example with h264 NALUs.
///     Call `AddBufferForDecode` to enqueue buffer for decode
///   * Output for buffers with processed encoded data. Consumer code have to use this to get processed buffers to reuse.
///     Call `GetEncodedBuffersForReuse` to get processed buffer
/// * IO for buffers with decoded data
///   * Output for buffers with decoded frames.
///     Call `WaitForDecodedBuffer` to sync wait for decoded frame
///   * Input for frame buffers for reuse
///     Call `ReuseDecodedBuffer` to add frame buffer to decoder
///
/// Always use `GetEncodedBuffersForReuse` and `ReuseDecodedBuffer` because it covers all cases - then decoders allocates frames by themselves or uses pre-allocated buffers.
/// </remarks>
public abstract class BaseDecoder<TInputBuffer, TOutputBuffer> : IDisposable, IDecoder
    where TInputBuffer: UniversalEncodedBuffer
{
    private readonly ILogger _logger;

    private readonly BlockingCollection<TInputBuffer> _encodedBuffersInput = new();
    private readonly BlockingCollection<TInputBuffer> _encodedBuffersOutput = new();

    private readonly BlockingCollection<TOutputBuffer> _decodedFramesOutput = new();

    private Thread? _decodingThread;
    private CancellationTokenSource? _cts;

    protected BaseDecoder(ILogger logger)
    {
        _logger = logger;
    }

    public virtual void Start()
    {
        _logger.LogInformation("Starting decoder");

        if (_decodingThread != null)
        {
            throw new InvalidOperationException("Decoding already started");
        }

        _cts = new CancellationTokenSource();

        _decodingThread = new Thread(() => DecodingThreadProc(_cts.Token))
        {
            Name = "DecoderThread",
            IsBackground = true,
            Priority = ThreadPriority.Highest
        };
        _decodingThread.Start();

        _logger.LogInformation("Decoder started");
    }

    public virtual void Stop()
    {
        _logger.LogInformation("Stopping decoder");

        if (_cts == null || _decodingThread == null)
        {
            return;
        }

        _cts.Cancel();

        if (_decodingThread.IsAlive)
        {
            if (!_decodingThread.Join(TimeSpan.FromSeconds(5)))
            {
                _logger.LogWarning("Decoder thread did not stop gracefully");
            }
        }

        FlushDecoder();

        _logger.LogInformation("Decoder stopped");
    }

    /// <summary>
    /// Client use this method to send data for decoding
    /// </summary>
    /// <param name="encodedBuffer">
    /// Buffer with encoded data. Usually NALU(now we have only h264 support...)
    /// </param>
    public void AddBufferForDecode(TInputBuffer encodedBuffer)
    {
        _encodedBuffersInput.Add(encodedBuffer);
    }

    /// <summary>
    /// Client uses this method to get free buffers that can be uses for <see cref="AddBufferForDecode"/>
    /// </summary>
    /// <returns>Free buffer or null if there is no free buffers</returns>
    public TInputBuffer? GetEncodedBuffersForReuse()
    {
        if (_encodedBuffersOutput.TryTake(out var item))
        {
            return item;
        }

        return null;
    }

    /// <summary>
    /// Blocking method that blocks while there is no decoded frames
    /// </summary>
    /// <returns>
    /// Frame with decoded data
    /// </returns>
    public TOutputBuffer WaitForDecodedFrames()
    {
        return _decodedFramesOutput.Take();
    }

    /// <summary>
    /// Displayed frames have to be returned for reuse
    /// </summary>
    /// <param name="decodedFrame">
    /// </param>
    public abstract void ReuseDecodedFrame(TOutputBuffer decodedFrame);

    /// <summary>
    /// Implementations of this methods will pass buffers to real decoders(ffmpeg, v4l2, va-api)
    /// </summary>
    /// <param name="encodedBuffer">
    /// Buffer with encoded data
    /// </param>
    protected abstract void ProcessEncodedDataBuffer(TInputBuffer encodedBuffer);

    protected abstract void FlushDecoder();

    /// <summary>
    /// Implementations have to call this method when there are encoded buffer that can be reused by class clients
    /// </summary>
    /// <param name="encodedBuffer">
    /// Free encoded buffer
    /// </param>
    protected void AddEncodedBufferToReuse(TInputBuffer encodedBuffer)
    {
        _encodedBuffersOutput.Add(encodedBuffer);
    }

    /// <summary>
    /// Implementations of <see cref="BaseDecoder"/> have to call this method to pass decoded frame to output
    /// </summary>
    /// <param name="decodedFrame">
    /// Decoded frame
    /// </param>
    protected void AddDecodedFrameToOutput(TOutputBuffer decodedFrame)
    {
        _decodedFramesOutput.Add(decodedFrame);
    }

    /// <summary>
    /// Gets the pixel format of decoded frames output by this decoder.
    /// This is known at construction time and does not change.
    /// </summary>
    public abstract PixelFormat OutputPixelFormat { get; }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _encodedBuffersInput.Dispose();
            _encodedBuffersOutput.Dispose();
            //_decodedFramesInput.Dispose();
            _decodedFramesOutput.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void DecodingThreadProc(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                while (_encodedBuffersInput.TryTake(out var encodedBuffer))
                {
                    ProcessEncodedDataBuffer(encodedBuffer);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Decoding error");
        }
    }
}

public interface IDecoder
{

}