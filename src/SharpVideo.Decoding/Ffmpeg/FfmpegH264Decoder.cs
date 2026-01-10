using System.Collections.Concurrent;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;

namespace SharpVideo.Decoding.Ffmpeg;

public sealed unsafe class FfmpegH264Decoder : BaseDecoder<FfmpegDecodedFrame>
{
    private AVCodec* _codec;
    private AVCodecContext* _codecContext;
    private AVPacket* _packet;
    private readonly ILogger<FfmpegH264Decoder> _logger;

    private bool _disposed;
    private readonly FfmpegFramesStorage _framesStorage;
    private readonly BlockingCollection<FfmpegDecodedFrame> _unusedFrames = new();
    private FfmpegH264Parser _parser;

    private FfmpegH264Decoder(
        AVCodec* codec,
        AVCodecContext* codecContext,
        AVPacket* packet,
        FfmpegH264Parser parser,
        ILogger<FfmpegH264Decoder> logger)
        : base(logger)
    {
        _codec = codec;
        _codecContext = codecContext;
        _packet = packet;
        _parser = parser;
        _logger = logger;

        // Preallocate some buffers for decoded data
        var framesCount = 3;
        _framesStorage = new FfmpegFramesStorage(framesCount);
        foreach (var decodedFrame in _framesStorage.GetAllWrappers())
        {
            _unusedFrames.Add(decodedFrame);
        }
        _logger.LogInformation("Allocated {Count} ffmpeg frames", framesCount);
    }

    public static FfmpegH264Decoder Create(ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<FfmpegH264Decoder>();

        var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
        if (codec == null)
        {
            throw new Exception($"Failed to find {AVCodecID.AV_CODEC_ID_H264}");
        }

        logger.LogInformation("Found codec: {CodecName}", new string((sbyte*)codec->name));

        var codecContext = ffmpeg.avcodec_alloc_context3(codec);
        if (codecContext == null)
        {
            throw new Exception("Failed to create codec context");
        }

        logger.LogInformation("Codec context allocated");

        var ret = ffmpeg.avcodec_open2(codecContext, codec, null);
        if (ret < 0)
        {
            throw new Exception("Failed to open coded");
        }

        logger.LogInformation("Codec opened successfully");

        var packet = ffmpeg.av_packet_alloc();
        if (packet == null)
        {
            throw new Exception("Failed to allocate packet");
        }

        logger.LogInformation("Packet allocated");

        var parser = new FfmpegH264Parser(AVCodecID.AV_CODEC_ID_H264, logger);

        return new FfmpegH264Decoder(codec, codecContext, packet, parser, logger);
    }

    /// <inheritdoc />
    public override void Decode(ReadOnlySpan<byte> nalu)
    {
        var parseResult = _parser.Parse(nalu, _codecContext);
        if (parseResult == null)
        {
            // Parser hasn't accumulated enough data yet
            return;
        }

        // If parser returned error or empty packet, skip
        if (parseResult.Data == null || parseResult.Size == 0)
        {
            return;
        }

        // Send packet to decoder
        _packet->data = parseResult.Data;
        _packet->size = parseResult.Size;

        var ret = ffmpeg.avcodec_send_packet(_codecContext, _packet);
        if (ret < 0)
        {
            _logger.LogError("Error sending packet: {Error}", GetErrorString(ret));
        }
        else
        {
            _logger.LogTrace("Packet sent");
        }

        // Receive frames from decoder
        ReceiveFrames();
    }

    /// <inheritdoc />
    public override void ReuseDecodedFrame(FfmpegDecodedFrame decodedFrame)
    {
        ffmpeg.av_frame_unref(decodedFrame.Frame);
        _unusedFrames.Add(decodedFrame);
    }

    /// <inheritdoc />
    protected override void FlushDecoder()
    {
        if (_codecContext == null)
        {
            return;
        }

        // Send null packet to flush decoder
        var ret = ffmpeg.avcodec_send_packet(_codecContext, null);
        if (ret < 0)
        {
            _logger.LogError("Error flushing decoder: {Error}", GetErrorString(ret));
            return;
        }

        // Receive remaining frames
        int flushedFrames = 0;
        while (true)
        {
            var frameWrapper = _unusedFrames.Take();
            ret = ffmpeg.avcodec_receive_frame(_codecContext, frameWrapper.Frame);
            if (ret == ffmpeg.AVERROR_EOF || ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                _unusedFrames.Add(frameWrapper);
                break;
            }

            if (ret < 0)
            {
                _unusedFrames.Add(frameWrapper);
                _logger.LogError("Error receiving flushed frame: {Error}", GetErrorString(ret));
                break;
            }

            flushedFrames++;
            AddDecodedFrameToOutput(frameWrapper);
        }

        _logger.LogInformation("Flushed {FlushedFrames} remaining frame(s)", flushedFrames);
    }

    /// <summary>
    /// FFmpeg software H.264 decoder always outputs YUV420P format.
    /// </summary>
    public override PixelFormat OutputPixelFormat => KnownPixelFormats.DRM_FORMAT_YUV420;

    private void ReceiveFrames()
    {
        while (true)
        {
            var frameWrapper = _unusedFrames.Take();
            var ret = ffmpeg.avcodec_receive_frame(_codecContext, frameWrapper.Frame);
            if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
            {
                // No data. Frame can be reused
                _unusedFrames.Add(frameWrapper);
                break;
            }

            if (ret < 0)
            {
                // Error. Frame can be reused
                _unusedFrames.Add(frameWrapper);
                _logger.LogError("Error receiving frame: {Error}", GetErrorString(ret));
                break;
            }

            AddDecodedFrameToOutput(frameWrapper);
        }
    }

    private static string GetErrorString(int errorCode)
    {
        byte* buffer = stackalloc byte[ffmpeg.AV_ERROR_MAX_STRING_SIZE];
        ffmpeg.av_strerror(errorCode, buffer, (ulong)ffmpeg.AV_ERROR_MAX_STRING_SIZE);
        return new string((sbyte*)buffer);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CleanupNativeResources();
        }

        base.Dispose(disposing);
    }

    private void CleanupNativeResources()
    {
        _parser?.Dispose();
        _parser = null;

        foreach (var wrapper in _framesStorage.GetAllWrappers())
        {
            var frame = wrapper.Frame;
            ffmpeg.av_frame_free(&frame);
        }

        if (_packet != null)
        {
            var packet = _packet;
            ffmpeg.av_packet_free(&packet);
            _packet = null;
        }

        if (_codecContext != null)
        {
            var ctx = _codecContext;
            ffmpeg.avcodec_free_context(&ctx);
            _codecContext = null;
        }
    }
}