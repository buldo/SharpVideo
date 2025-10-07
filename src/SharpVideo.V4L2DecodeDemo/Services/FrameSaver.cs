using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.V4L2DecodeDemo.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SharpVideo.V4L2DecodeDemo.Services;

[SupportedOSPlatform("linux")]
public class FrameSaver : IDisposable
{
    private readonly BlockingCollection<FrameDecodedEventArgs> _frameQueue;
    private readonly Thread _processingThread;
    private readonly CancellationTokenSource _cts;
    private readonly ILogger _logger;
    private readonly string _outputDir;
    private bool _disposed;

    public FrameSaver(string outputDir, ILogger logger, int queueCapacity = 100)
    {
        _outputDir = outputDir;
        _logger = logger;
        _frameQueue = new BlockingCollection<FrameDecodedEventArgs>(queueCapacity);
        _cts = new CancellationTokenSource();

        Directory.CreateDirectory(_outputDir);

        _processingThread = new Thread(ProcessFramesThreadProc)
        {
            Name = "FrameSaver",
            IsBackground = true
        };
        _processingThread.Start();

        _logger.LogInformation("FrameSaver started, saving to: {OutputDir}", _outputDir);
    }

    public bool TryEnqueueFrame(FrameDecodedEventArgs frameData)
    {
        if (_disposed)
            return false;

        // Try to add without blocking - if queue is full, skip this frame
        if (_frameQueue.TryAdd(frameData, 0))
        {
            return true;
        }

        _logger.LogWarning("Frame queue full, skipping frame {FrameNumber}", frameData.FrameNumber);
        return false;
    }

    private void ProcessFramesThreadProc()
    {
        _logger.LogInformation("Frame processing thread started");

        try
        {
            foreach (var frameData in _frameQueue.GetConsumingEnumerable(_cts.Token))
            {
                try
                {
                    SaveFrameAsImage(frameData);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save frame {FrameNumber}", frameData.FrameNumber);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Frame processing cancelled");
        }

        _logger.LogInformation("Frame processing thread stopped");
    }

    private void SaveFrameAsImage(FrameDecodedEventArgs e)
    {
        // Extract frame data only when we're ready to process it
        if (e.ExtractFrameData == null)
        {
            _logger.LogWarning("No frame data extractor available for frame {FrameNumber}", e.FrameNumber);
            return;
        }

        var frameData = e.ExtractFrameData();

        var outputPath = Path.Combine(_outputDir, $"frame_{e.FrameNumber:D5}.jpg");

        // Convert NV12 to RGB (assuming NV12 format - most common for H.264 decoding)
        // NV12 format: Y plane followed by interleaved UV plane
        var width = e.Width;
        var height = e.Height;

        using var image = new Image<Rgb24>(width, height);

        var yPlaneSize = width * height;
        var uvPlaneSize = yPlaneSize / 2;

        if (frameData.Length < yPlaneSize + uvPlaneSize)
        {
            _logger.LogWarning("Frame data too small for NV12 conversion: {Size} bytes (expected at least {Expected})",
                frameData.Length, yPlaneSize + uvPlaneSize);
            return;
        }

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var pixelRow = accessor.GetRowSpan(y);

                for (int x = 0; x < width; x++)
                {
                    // Get Y value
                    int yIndex = y * width + x;
                    int yValue = frameData[yIndex];

                    // Get UV values (subsampled 2x2)
                    int uvY = y / 2;
                    int uvX = (x / 2) * 2;
                    int uvIndex = yPlaneSize + uvY * width + uvX;

                    int uValue = uvIndex < frameData.Length ? frameData[uvIndex] : 128;
                    int vValue = uvIndex + 1 < frameData.Length ? frameData[uvIndex + 1] : 128;

                    // YUV to RGB conversion (ITU-R BT.601)
                    int c = yValue - 16;
                    int d = uValue - 128;
                    int ee = vValue - 128;

                    int r = (298 * c + 409 * ee + 128) >> 8;
                    int g = (298 * c - 100 * d - 208 * ee + 128) >> 8;
                    int b = (298 * c + 516 * d + 128) >> 8;

                    // Clamp values
                    r = Math.Clamp(r, 0, 255);
                    g = Math.Clamp(g, 0, 255);
                    b = Math.Clamp(b, 0, 255);

                    pixelRow[x] = new Rgb24((byte)r, (byte)g, (byte)b);
                }
            }
        });

        image.SaveAsJpeg(outputPath);
        _logger.LogDebug("Saved frame {FrameNumber} to {Path}", e.FrameNumber, outputPath);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Complete the queue and wait for processing to finish
        _frameQueue.CompleteAdding();

        if (_processingThread.IsAlive)
        {
            if (!_processingThread.Join(TimeSpan.FromSeconds(10)))
            {
                _logger.LogWarning("Frame processing thread did not complete in time, cancelling");
                _cts.Cancel();
                _processingThread.Join(TimeSpan.FromSeconds(2));
            }
        }

        _cts.Dispose();
        _frameQueue.Dispose();

        _logger.LogInformation("FrameSaver disposed");
    }
}
