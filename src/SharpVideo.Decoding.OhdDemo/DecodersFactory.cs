using System.Runtime.InteropServices;

using FFmpeg.AutoGen;

using SharpVideo.Decoding.Ffmpeg;
using SharpVideo.Decoding.V4l2;
using SharpVideo.Decoding.V4l2.Discovery;
using SharpVideo.Decoding.V4l2.Stateful;
using SharpVideo.Decoding.V4l2.Stateless;
using SharpVideo.FfmpegBin;

namespace SharpVideo.Decoding.OhdDemo;

/// <summary>
/// Factory for creating H264 decoders with automatic hardware detection.
/// </summary>
public class DecodersFactory
{
    private readonly V4l2H264DecoderProvider _v4L2Provider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DecodersFactory> _logger;

    private bool _ffmpegLoaded;
    private V4l2H264DecoderInfo? _cachedV4l2DecoderInfo;
    private bool _v4l2DiscoveryDone;

    public DecodersFactory(
        V4l2H264DecoderProvider v4L2Provider,
        ILoggerFactory loggerFactory)
    {
        _v4L2Provider = v4L2Provider;
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<DecodersFactory>();
    }

    /// <summary>
    /// Creates an FFmpeg-based H264 decoder.
    /// </summary>
    /// <returns>An FFmpeg H264 decoder instance.</returns>
    public FfmpegH264Decoder CreateFfmpegDecoder()
    {
        EnsureFfmpegLoaded();
        return FfmpegH264Decoder.Create(_loggerFactory);
    }

    /// <summary>
    /// Attempts to create a V4L2 hardware decoder.
    /// </summary>
    /// <returns>A V4L2 decoder if available, null otherwise.</returns>
    public IDecoder? TryCreateV4l2Decoder()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _logger.LogDebug("V4L2 decoders are only available on Linux");
            return null;
        }

        var decoderInfo = GetCachedV4l2DecoderInfo();
        if (decoderInfo == null)
        {
            return null;
        }

        return CreateV4l2DecoderFromInfo(decoderInfo);
    }

    /// <summary>
    /// Gets information about the best available V4L2 decoder, if any.
    /// </summary>
    /// <returns>Decoder info or null if no V4L2 decoder is available.</returns>
    public V4l2H264DecoderInfo? GetAvailableV4l2DecoderInfo()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return null;
        }

        return GetCachedV4l2DecoderInfo();
    }

    /// <summary>
    /// Discovers all available V4L2 H264 decoders.
    /// </summary>
    /// <returns>List of available decoder information.</returns>
    public IReadOnlyList<V4l2H264DecoderInfo> DiscoverAllV4l2Decoders()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return [];
        }

        return _v4L2Provider.DiscoverDecoders();
    }

    private V4l2H264DecoderInfo? GetCachedV4l2DecoderInfo()
    {
        if (!_v4l2DiscoveryDone)
        {
            _cachedV4l2DecoderInfo = _v4L2Provider.FindBestDecoder();
            _v4l2DiscoveryDone = true;

            if (_cachedV4l2DecoderInfo != null)
            {
                _logger.LogInformation(
                    "Best V4L2 decoder: {Type} at {Path} ({Driver})",
                    _cachedV4l2DecoderInfo.DecoderType,
                    _cachedV4l2DecoderInfo.DevicePath,
                    _cachedV4l2DecoderInfo.Driver);
            }
            else
            {
                _logger.LogDebug("No V4L2 H264 decoder found on this system");
            }
        }

        return _cachedV4l2DecoderInfo;
    }

    private IDecoder CreateV4l2DecoderFromInfo(V4l2H264DecoderInfo decoderInfo)
    {
        return decoderInfo.DecoderType switch
        {
            V4l2H264DecoderType.Stateless => V4l2H264StatelessDecoder.Create(_loggerFactory, decoderInfo),
            //V4l2H264DecoderType.Stateful => V4l2H264StatefulDecoder.Create(_loggerFactory, decoderInfo),
            _ => throw new InvalidOperationException($"Unexpected decoder type: {decoderInfo.DecoderType}")
        };
    }

    private void EnsureFfmpegLoaded()
    {
        if (!_ffmpegLoaded)
        {
            var ffmpegPath = FfmpegLoader.Load(_logger);
            if (ffmpegPath != null)
            {
                ffmpeg.RootPath = ffmpegPath;
            }

            _ffmpegLoaded = true;
        }
    }
}