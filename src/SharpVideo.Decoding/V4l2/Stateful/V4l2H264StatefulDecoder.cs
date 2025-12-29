using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Drm;

namespace SharpVideo.Decoding.V4l2.Stateful;

/// <summary>
/// V4L2 stateful H264 decoder.
/// Used for hardware decoders that manage decoding state internally
/// (e.g., Qualcomm Venus).
/// </summary>
[SupportedOSPlatform("linux")]
public class V4l2H264StatefulDecoder : BaseDecoder
{
    private readonly string _devicePath;

    private V4l2H264StatefulDecoder(
        ILogger<V4l2H264StatefulDecoder> logger,
        string devicePath)
        : base(logger)
    {
        _devicePath = devicePath;
    }

    /// <summary>
    /// Creates a stateful H264 decoder using the specified device.
    /// </summary>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    /// <param name="decoderInfo">Decoder information from discovery.</param>
    /// <returns>A new stateful decoder instance.</returns>
    public static V4l2H264StatefulDecoder Create(
        ILoggerFactory loggerFactory,
        V4l2H264DecoderInfo decoderInfo)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(decoderInfo);

        if (decoderInfo.DecoderType != V4l2H264DecoderType.Stateful)
        {
            throw new ArgumentException(
                $"Expected stateful decoder info, got {decoderInfo.DecoderType}",
                nameof(decoderInfo));
        }

        var logger = loggerFactory.CreateLogger<V4l2H264StatefulDecoder>();
        logger.LogInformation(
            "Creating V4L2 stateful H264 decoder at {DevicePath} ({Driver}: {Card})",
            decoderInfo.DevicePath,
            decoderInfo.Driver,
            decoderInfo.Card);

        return new V4l2H264StatefulDecoder(logger, decoderInfo.DevicePath);
    }

    /// <summary>
    /// Gets the device path used by this decoder.
    /// </summary>
    public string DevicePath => _devicePath;

    public override void ReuseDecodedFrame(UniversalDecodedFrame decodedFrame)
    {
        throw new NotImplementedException();
    }

    protected override void ProcessEncodedDataBuffer(UniversalEncodedBuffer encodedBuffer)
    {
        throw new NotImplementedException();
    }

    protected override void FlushDecoder()
    {
        throw new NotImplementedException();
    }

    public override PixelFormat OutputPixelFormat { get; }
}