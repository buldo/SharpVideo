using System.Buffers;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;

namespace SharpVideo.Decoding.Ffmpeg;

internal sealed unsafe class FfmpegH264Parser : IDisposable
{
    private readonly AVCodecParserContext* _parserContext;
    private readonly ILogger _logger;
    private readonly List<ManagedMemoryEncodedBuffer> _accumulatedBuffers = new();
    private readonly ManagedMemoryEncodedBuffer _aggregationBuffer;
    private bool _disposed;

    public FfmpegH264Parser(AVCodecID codecId, ILogger logger)
    {
        _logger = logger;
        _parserContext = ffmpeg.av_parser_init((int)codecId);

        if (_parserContext == null)
        {
            throw new InvalidOperationException($"Failed to initialize parser for codec {codecId}");
        }

        _parserContext->flags |= ffmpeg.PARSER_FLAG_COMPLETE_FRAMES;
        _aggregationBuffer = new ManagedMemoryEncodedBuffer(2 * 1024 * 1024);

        _logger.LogInformation("H264 parser initialized with PARSER_FLAG_COMPLETE_FRAMES");
    }

    public ParsedPacketResult? Parse(ManagedMemoryEncodedBuffer buffer, AVCodecContext* codecContext)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FfmpegH264Parser));
        }

        _accumulatedBuffers.Add(buffer);
        _aggregationBuffer.AggregateInCurrent(_accumulatedBuffers);

        var dataSpan = _aggregationBuffer.Get();

        fixed (byte* dataPtr = dataSpan)
        {
            byte* outData = null;
            int outSize = 0;

            var parsedBytes = ffmpeg.av_parser_parse2(
                _parserContext,
                codecContext,
                &outData,
                &outSize,
                dataPtr,
                dataSpan.Length,
                ffmpeg.AV_NOPTS_VALUE,
                ffmpeg.AV_NOPTS_VALUE,
                0);

            if (parsedBytes < 0)
            {
                _logger.LogWarning("Parser error: {Error}", GetErrorString(parsedBytes));
                var buffersToReturn = _accumulatedBuffers.ToList();
                _accumulatedBuffers.Clear();
                return new ParsedPacketResult(null, 0, buffersToReturn);
            }

            if (outSize > 0)
            {
                var buffersToReturn = _accumulatedBuffers.ToList();
                _accumulatedBuffers.Clear();
                return new ParsedPacketResult(outData, outSize, buffersToReturn);
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

        _accumulatedBuffers.Clear();
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
    public List<ManagedMemoryEncodedBuffer> Buffers { get; }

    public unsafe ParsedPacketResult(byte* data, int size, List<ManagedMemoryEncodedBuffer> buffers)
    {
        Data = data;
        Size = size;
        Buffers = buffers;
    }
}
