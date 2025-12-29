using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Drm;

namespace SharpVideo.Decoding.V4l2.Stateless;

/// <summary>
/// V4L2 stateless H264 decoder.
/// Used for hardware decoders that require userspace to manage decoding state
/// (e.g., Raspberry Pi, Rockchip RK3588).
/// </summary>
[SupportedOSPlatform("linux")]
public class V4l2H264StatelessDecoder : BaseDecoder
{
    private readonly string _devicePath;
    private readonly string? _mediaDevicePath;

    private V4l2H264StatelessDecoder(
        ILogger<V4l2H264StatelessDecoder> logger,
        string devicePath,
        string? mediaDevicePath)
        : base(logger)
    {
        _devicePath = devicePath;
        _mediaDevicePath = mediaDevicePath;
    }

    /// <summary>
    /// Creates a stateless H264 decoder using the specified device.
    /// </summary>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    /// <param name="decoderInfo">Decoder information from discovery.</param>
    /// <returns>A new stateless decoder instance.</returns>
    public static V4l2H264StatelessDecoder Create(
        ILoggerFactory loggerFactory,
        V4l2H264DecoderInfo decoderInfo)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(decoderInfo);

        if (decoderInfo.DecoderType != V4l2H264DecoderType.Stateless)
        {
            throw new ArgumentException(
                $"Expected stateless decoder info, got {decoderInfo.DecoderType}",
                nameof(decoderInfo));
        }

        var logger = loggerFactory.CreateLogger<V4l2H264StatelessDecoder>();
        logger.LogInformation(
            "Creating V4L2 stateless H264 decoder at {DevicePath} ({Driver}: {Card})",
            decoderInfo.DevicePath,
            decoderInfo.Driver,
            decoderInfo.Card);

        return new V4l2H264StatelessDecoder(
            logger,
            decoderInfo.DevicePath,
            decoderInfo.MediaDevicePath);
    }

    /// <summary>
    /// Gets the device path used by this decoder.
    /// </summary>
    public string DevicePath => _devicePath;

    /// <summary>
    /// Gets the media device path, if available.
    /// </summary>
    public string? MediaDevicePath => _mediaDevicePath;

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