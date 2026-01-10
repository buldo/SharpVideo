using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;

namespace SharpVideo.Decoding.Ffmpeg;

internal sealed unsafe class FfmpegH264Parser : IDisposable
{
    private readonly AVCodecParserContext* _parserContext;
    private readonly ILogger _logger;
    private readonly byte[] _aggregationBuffer;
    private int _aggregationBufferUsed;
    private bool _disposed;

    public FfmpegH264Parser(AVCodecID codecId, ILogger logger)
    {
        _logger = logger;
        _parserContext = ffmpeg.av_parser_init((int)codecId);

        if (_parserContext == null)
        {
            throw new InvalidOperationException($"Failed to initialize parser for codec {codecId}");
        }

        //_parserContext->flags |= ffmpeg.PARSER_FLAG_COMPLETE_FRAMES;
        _aggregationBuffer = GC.AllocateArray<byte>(2 * 1024 * 1024, pinned: true);

        _logger.LogDebug("H264 parser initialized");
    }

    /// <summary>
    /// Parses NALU data and returns parsed packet result when a complete frame is available.
    /// </summary>
    /// <param name="nalu">NALU data including start code.</param>
    /// <param name="codecContext">FFmpeg codec context.</param>
    /// <returns>Parsed packet result if complete frame available, null otherwise.</returns>
    public ParsedPacketResult? Parse(ReadOnlySpan<byte> nalu, AVCodecContext* codecContext)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FfmpegH264Parser));
        }

        // Copy incoming NALU to aggregation buffer
        if (_aggregationBufferUsed + nalu.Length > _aggregationBuffer.Length)
        {
            _logger.LogWarning("Aggregation buffer overflow, resetting");
            _aggregationBufferUsed = 0;
        }

        nalu.CopyTo(_aggregationBuffer.AsSpan(_aggregationBufferUsed));
        _aggregationBufferUsed += nalu.Length;

        fixed (byte* dataPtr = _aggregationBuffer)
        {
            byte* outData = null;
            int outSize = 0;

            var parsedBytes = ffmpeg.av_parser_parse2(
                _parserContext,
                codecContext,
                &outData,
                &outSize,
                dataPtr,
                _aggregationBufferUsed,
                ffmpeg.AV_NOPTS_VALUE,
                ffmpeg.AV_NOPTS_VALUE,
                0);

            if (parsedBytes < 0)
            {
                _logger.LogWarning("Parser error: {Error}", GetErrorString(parsedBytes));
                _aggregationBufferUsed = 0;
                return new ParsedPacketResult(null, 0);
            }

            // Remove consumed bytes from the aggregation buffer
            if (parsedBytes > 0)
            {
                var remaining = _aggregationBufferUsed - parsedBytes;
                if (remaining > 0)
                {
                    Buffer.BlockCopy(_aggregationBuffer, parsedBytes, _aggregationBuffer, 0, remaining);
                }
                _aggregationBufferUsed = remaining;
            }

            if (outSize > 0)
            {
                // Parser produced output, data pointer is valid until next Parse() call
                return new ParsedPacketResult(outData, outSize);
            }

            // Parser didn't return a packet yet, continue accumulating
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_parserContext != null)
        {
            var ctx = _parserContext;
            ffmpeg.av_parser_close(ctx);
        }

        _aggregationBufferUsed = 0;
    }

    private static string GetErrorString(int errorCode)
    {
        byte* buffer = stackalloc byte[ffmpeg.AV_ERROR_MAX_STRING_SIZE];
        ffmpeg.av_strerror(errorCode, buffer, (ulong)ffmpeg.AV_ERROR_MAX_STRING_SIZE);
        return new string((sbyte*)buffer);
    }
}

internal sealed class ParsedPacketResult
{
    public unsafe byte* Data { get; }
    public int Size { get; }

    public unsafe ParsedPacketResult(byte* data, int size)
    {
        Data = data;
        Size = size;
    }
}
