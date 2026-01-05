using SharpVideo.Decoding.V4l2.Stateless;

namespace SharpVideo.Decoding.OhdDemo.ImguiOsd;

internal class UiHostFactory
{
    private readonly InMemoryPipeStreamAccessor _h264Stream;
    private readonly DecodersFactory _decodersFactory;
    private readonly ILoggerFactory _loggerFactory;

    public UiHostFactory(
        [FromKeyedServices("h264-stream")] InMemoryPipeStreamAccessor h264Stream,
        DecodersFactory decodersFactory,
        ILoggerFactory loggerFactory)
    {
        _h264Stream = h264Stream;
        _decodersFactory = decodersFactory;
        _loggerFactory = loggerFactory;
    }

    public IUiHost CreateHost()
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateWindowed();
        }

        if (OperatingSystem.IsLinux())
        {
            var dmExists = Environment.GetEnvironmentVariable("DISPLAY") != null || Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != null;
            if (dmExists)
            {
                return CreateWindowed();
            }
        }

        // TODO: bad hardcode
        var v4l2Decoder = (V4l2H264StatelessDecoder)_decodersFactory.TryCreateV4l2Decoder();

        return new DrmHost(
            _h264Stream,
            v4l2Decoder,
            _loggerFactory,
            _loggerFactory.CreateLogger<DrmHost>());
    }

    private IUiHost CreateWindowed()
    {
        var decoder = _decodersFactory.CreateFfmpegDecoder();

        return new WindowedHost(
            _h264Stream,
            decoder,
            _loggerFactory,
            _loggerFactory.CreateLogger<WindowedHost>());
    }
}