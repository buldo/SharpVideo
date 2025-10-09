using System.Collections.Concurrent;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SharpVideo.V4L2DecodeDemo;

[SupportedOSPlatform("linux")]
public class FrameSaver : IDisposable
{
    private readonly BlockingCollection<(byte[] data, int widht, int height)> _frameQueue = new(500);
    private readonly Thread _processingThread;
    private readonly CancellationTokenSource _cts;
    private readonly ILogger _logger;
    private readonly string _outputDir;
    private bool _disposed;

    private int _frameNumber;

    public FrameSaver(string outputDir, ILogger logger)
    {
        _outputDir = outputDir;
        _logger = logger;
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

    public bool TryEnqueueFrame(ReadOnlySpan<byte> span, int width, int height)
    {
        if (_disposed)
            return false;

        // Try to add without blocking - if queue is full, skip this frame
        if (_frameQueue.TryAdd((span.ToArray(), width, height), 0))
        {
            return true;
        }

        return false;
    }

    private void ProcessFramesThreadProc()
    {
        _logger.LogInformation("Frame processing thread started");

        foreach (var frameData in _frameQueue.GetConsumingEnumerable(_cts.Token))
        {
            SaveFrameAsImage(frameData.data, frameData.widht, frameData.height);
        }

        _logger.LogInformation("Frame processing thread stopped");
    }

    private void SaveFrameAsImage(byte[] frameData, int width, int height)
    {
        var outputPath = Path.Combine(_outputDir, $"frame_{_frameNumber:D5}.jpg");

        // Convert NV12 to RGB (assuming NV12 format - most common for H.264 decoding)
        // NV12 format: Y plane followed by interleaved UV plane

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
        _logger.LogDebug("Saved frame {FrameNumber} to {Path}", _frameNumber, outputPath);
        _frameNumber++;
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