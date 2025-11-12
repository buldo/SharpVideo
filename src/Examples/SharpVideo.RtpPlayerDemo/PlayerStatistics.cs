using System.Diagnostics;
using System.Runtime.Versioning;

namespace SharpVideo.RtpPlayerDemo;

/// <summary>
/// Tracks player statistics for monitoring and OSD display
/// </summary>
[SupportedOSPlatform("linux")]
public class PlayerStatistics
{
    private int _decodedFrames;
    private int _presentedFrames;
    private readonly object _lock = new();
    private readonly Stopwatch _fpsStopwatch = Stopwatch.StartNew();
    private int _lastDecodedFrames;
    private int _lastPresentedFrames;
    private double _currentDecodeFps;
    private double _currentPresentFps;
    private TimeSpan _lastFpsUpdate = TimeSpan.Zero;

    /// <summary>
    /// Total number of decoded frames
    /// </summary>
    public int DecodedFrames
    {
        get { lock (_lock) return _decodedFrames; }
    }

    /// <summary>
    /// Total number of presented frames
    /// </summary>
    public int PresentedFrames
    {
        get { lock (_lock) return _presentedFrames; }
    }

    /// <summary>
    /// Total decode elapsed time
    /// </summary>
    public TimeSpan DecodeElapsed { get; set; }

    /// <summary>
    /// Total present elapsed time
    /// </summary>
    public TimeSpan PresentElapsed { get; set; }

    /// <summary>
    /// Average decode time per frame in milliseconds
    /// </summary>
    public double AverageDecodeTimeMs
    {
        get
        {
            lock (_lock)
            {
                if (_decodedFrames == 0) return 0;
                return DecodeElapsed.TotalMilliseconds / _decodedFrames;
            }
        }
    }

    /// <summary>
    /// Current decode FPS (updated every second)
    /// </summary>
    public double CurrentDecodeFps
    {
        get { lock (_lock) return _currentDecodeFps; }
    }

    /// <summary>
    /// Current present FPS (updated every second)
    /// </summary>
    public double CurrentPresentFps
    {
        get { lock (_lock) return _currentPresentFps; }
    }

    /// <summary>
    /// Average decode FPS over total time
    /// </summary>
    public double AverageDecodeFps
    {
        get
        {
            lock (_lock)
            {
                if (DecodeElapsed.TotalSeconds == 0) return 0;
                return _decodedFrames / DecodeElapsed.TotalSeconds;
            }
        }
    }

    /// <summary>
    /// Average present FPS over total time
    /// </summary>
    public double AveragePresentFps
    {
        get
        {
            lock (_lock)
            {
                if (PresentElapsed.TotalSeconds == 0) return 0;
                return _presentedFrames / PresentElapsed.TotalSeconds;
            }
        }
    }

    public void IncrementDecodedFrames()
    {
        lock (_lock)
        {
            _decodedFrames++;
        }
    }

    public void IncrementPresentedFrames()
    {
        lock (_lock)
        {
            _presentedFrames++;
        }
    }

    /// <summary>
    /// Update FPS counters (should be called periodically, e.g., every frame)
    /// </summary>
    public void UpdateFps()
    {
        var elapsed = _fpsStopwatch.Elapsed;
        var timeSinceLastUpdate = elapsed - _lastFpsUpdate;

        // Update FPS every second
        if (timeSinceLastUpdate.TotalSeconds >= 1.0)
        {
            lock (_lock)
            {
                var decodedDelta = _decodedFrames - _lastDecodedFrames;
                var presentedDelta = _presentedFrames - _lastPresentedFrames;

                _currentDecodeFps = decodedDelta / timeSinceLastUpdate.TotalSeconds;
                _currentPresentFps = presentedDelta / timeSinceLastUpdate.TotalSeconds;

                _lastDecodedFrames = _decodedFrames;
                _lastPresentedFrames = _presentedFrames;
                _lastFpsUpdate = elapsed;
            }
        }
    }

    /// <summary>
    /// Get formatted statistics string
    /// </summary>
    public string GetSummary()
    {
        lock (_lock)
        {
            return $"Decoded: {_decodedFrames} frames @ {AverageDecodeFps:F2} FPS (avg), {_currentDecodeFps:F2} FPS (current)\n" +
                   $"Presented: {_presentedFrames} frames @ {AveragePresentFps:F2} FPS (avg), {_currentPresentFps:F2} FPS (current)\n" +
                   $"Avg decode time: {AverageDecodeTimeMs:F2} ms/frame";
        }
    }
}
