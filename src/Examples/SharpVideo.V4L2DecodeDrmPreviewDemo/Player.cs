using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

using Microsoft.Extensions.Logging;

using SharpVideo.Drm;
using SharpVideo.Utils;
using SharpVideo.V4L2StatelessDecoder.Services;

namespace SharpVideo.V4L2DecodeDrmPreviewDemo;

[SupportedOSPlatform("linux")]
public class Player
{
    private readonly DrmPresenter _presenter;
    private readonly H264V4L2StatelessDecoder _decoder;
    private readonly ILogger<Player> _logger;
    private readonly BlockingCollection<SharedDmaBuffer> _buffersToPresent = new();
    private readonly CancellationTokenSource displayCts = new CancellationTokenSource();

    private Task _decodeTask;
    private Task _displayTask;

    private ManualResetEventSlim _decodeCompleted = new(false);

    public Player(
        DrmPresenter presenter,
        H264V4L2StatelessDecoder decoder,
        ILogger<Player> logger)
    {
        _presenter = presenter;
        _decoder = decoder;
        _logger = logger;
    }

    public PlayerStatistics Statistics { get; } = new();

    public void Init()
    {
        _decoder.InitializeDecoder(ProcessBuffer);
    }

    public void StartPlay(FileStream fileStream)
    {
        _decodeTask = Task.Run(() => DecodeLocalAsync(fileStream));
        _displayTask = Task.Run(() => DisplayRoutine(displayCts.Token));
    }

    public void WaitCompleted()
    {
        _decodeCompleted.Wait();
        displayCts.Cancel(false);
        Task.WaitAll(_decodeTask, _displayTask);
        Statistics.DecodeElapsed = _decoder.Statistics.DecodeElapsed;
    }

    private void ProcessBuffer(SharedDmaBuffer buffer)
    {
        Statistics.IncrementDecodedFrames();
        _buffersToPresent.Add(buffer);
    }

    private async Task DecodeLocalAsync(FileStream fileStream)
    {
        await _decoder.DecodeStreamAsync(fileStream);
        _decodeCompleted.Set();
    }

    private void DisplayRoutine(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Display thread started");
        var displayStopwatch = Stopwatch.StartNew();
        while(!(cancellationToken.IsCancellationRequested && Statistics.DecodedFrames == Statistics.PresentedFrames))
        {
            SharedDmaBuffer buffer;
            try
            {
                buffer = _buffersToPresent.Take(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _presenter.SetOverlayPlane(buffer);
            Statistics.IncrementPresentedFrames();
            var toRequeue = _presenter.GetPresentedFrames();
            for (int i = 0; i < toRequeue.Length; i++)
            {
                _decoder.RequeueCaptureBuffer(toRequeue[i]);
            }
        }
        displayStopwatch.Stop();

        _logger.LogInformation("Display thread stopped. Frames: {FrameCount}; Time: {Elapsed}s)", Statistics.PresentedFrames, displayStopwatch.Elapsed.TotalSeconds);
    }

}