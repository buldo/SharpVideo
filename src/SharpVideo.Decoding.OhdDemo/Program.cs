using SharpVideo.Decoding.OhdDemo.Configuration;
using SharpVideo.Decoding.OhdDemo.ImguiOsd;
using SharpVideo.Decoding.OhdDemo.TestRx;
using SharpVideo.Decoding.V4l2.Discovery;

namespace SharpVideo.Decoding.OhdDemo;

internal class Program
{
    static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging
            .AddConsole();

        builder.Services.Configure<RemoteOpenHdConfiguration>(
            builder.Configuration.GetSection(RemoteOpenHdConfiguration.Key));

        builder.Services.AddKeyedSingleton<InMemoryPipeStreamAccessor>("h264-stream");
        builder.Services.AddSingleton<V4l2H264DecoderProvider>();
        builder.Services.AddSingleton<DecodersFactory>();

        builder.Services.AddSingleton<UiHostFactory>();
        builder.Services.AddHostedService<UiHostBase>(CreateUiHost);

        builder.Services.AddHostedService<RemoteOpenHdConnector>();

        var host = builder.Build();

        host.Run();
    }

    private static UiHostBase CreateUiHost(IServiceProvider sp)
    {
        var factory = sp.GetRequiredService<UiHostFactory>();
        return factory.CreateHost();
    }
}