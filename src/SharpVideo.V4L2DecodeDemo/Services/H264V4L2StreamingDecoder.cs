using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SharpVideo.Linux.Native;
using SharpVideo.V4L2DecodeDemo.Interfaces;
using SharpVideo.V4L2DecodeDemo.Models;

namespace SharpVideo.V4L2DecodeDemo.Services;

/// <summary>
/// High-performance streaming H.264 decoder using V4L2 hardware acceleration.
/// Implements enterprise-grade patterns with proper resource management, error handling, and extensibility.
/// </summary>
public class H264V4L2StreamingDecoder : IVideoDecoder
{
    #region Fields and Events

    private readonly ILogger<H264V4L2StreamingDecoder> _logger;
    private readonly IV4L2DeviceManager _deviceManager;
    private readonly DecoderConfiguration _configuration;
    
    private int _deviceFd = -1;
    private readonly List<MappedBuffer> _outputBuffers = new();
    private readonly List<MappedBuffer> _captureBuffers = new();
    private bool _disposed;
    private uint _outputBufferCount;
    private uint _captureBufferCount;
    private int _framesDecoded;

    public event EventHandler<FrameDecodedEventArgs>? FrameDecoded;
    public event EventHandler<DecodingProgressEventArgs>? ProgressChanged;

    #endregion

    #region Constructor and Configuration

    public H264V4L2StreamingDecoder(ILogger<H264V4L2StreamingDecoder> logger)
        : this(logger, null, null)
    {
    }

    public H264V4L2StreamingDecoder(
        ILogger<H264V4L2StreamingDecoder> logger,
        IV4L2DeviceManager? deviceManager = null,
        DecoderConfiguration? configuration = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? new DecoderConfiguration();
        
        var deviceLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<V4L2DeviceManager>();
        _deviceManager = deviceManager ?? new V4L2DeviceManager(deviceLogger, _configuration);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Decodes an H.264 file using V4L2 hardware acceleration
    /// </summary>
    public async Task DecodeFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Video file not found: {filePath}");

        _logger.LogInformation("Starting H.264 decode of {FilePath}", filePath);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Initialize decoder
            await InitializeDecoderAsync(cancellationToken);

            // Process the file
            await ProcessVideoFileAsync(filePath, cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation("Decoding completed successfully. {FrameCount} frames in {ElapsedTime:F2}s ({FPS:F2} fps)",
                _framesDecoded, stopwatch.Elapsed.TotalSeconds, _framesDecoded / stopwatch.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during H.264 decoding");
            throw;
        }
        finally
        {
            await CleanupAsync();
        }
    }

    #endregion

    #region Initialization

    private async Task InitializeDecoderAsync(CancellationToken cancellationToken)
    {
        // Find suitable decoder device
        var devicePath = await _deviceManager.FindH264DecoderDeviceAsync();
        if (string.IsNullOrEmpty(devicePath))
        {
            throw new InvalidOperationException("No suitable H.264 decoder device found");
        }

        // Open device
        _deviceFd = Libc.open(devicePath, OpenFlags.O_RDWR | OpenFlags.O_NONBLOCK);
        if (_deviceFd < 0)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to open device {devicePath}. Error: {error}");
        }

        _logger.LogInformation("Opened decoder device: {DevicePath} (fd: {FileDescriptor})", devicePath, _deviceFd);

        try
        {
            // Configure decoder formats
            await ConfigureFormatsAsync(cancellationToken);

            // Setup buffers
            await SetupBuffersAsync(cancellationToken);

            // Start streaming
            await StartStreamingAsync(cancellationToken);

            _logger.LogInformation("Decoder initialization completed successfully");
        }
        catch
        {
            // Cleanup on failure
            if (_deviceFd >= 0)
            {
                Libc.close(_deviceFd);
                _deviceFd = -1;
            }
            throw;
        }
    }

    private async Task ConfigureFormatsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Configuring decoder formats...");

        // Configure output format (H.264 input)
        var outputFormat = new V4L2Format
        {
            Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE
        };

        var outputPixMp = new V4L2PixFormatMplane
        {
            Width = _configuration.InitialWidth,
            Height = _configuration.InitialHeight,
            PixelFormat = 0x34363253, // H264 Parsed Slice Data
            NumPlanes = 1,
            Field = (uint)V4L2Field.NONE
        };
        outputFormat.Pix_mp = outputPixMp;

        var result = LibV4L2.SetFormat(_deviceFd, ref outputFormat);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to set output format: {result.ErrorMessage}");
        }

        _logger.LogInformation("Set output format: {Width}x{Height}, PixelFormat: 0x{PixelFormat:X8}",
            outputFormat.Pix_mp.Width, outputFormat.Pix_mp.Height, outputFormat.Pix_mp.PixelFormat);

        // Configure capture format (decoded output)
        await ConfigureCaptureFormatAsync(cancellationToken);
    }

    private async Task ConfigureCaptureFormatAsync(CancellationToken cancellationToken)
    {
        var captureFormat = new V4L2Format
        {
            Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE
        };

        // Try preferred format first
        var capturePixMp = new V4L2PixFormatMplane
        {
            Width = _configuration.InitialWidth,
            Height = _configuration.InitialHeight,
            PixelFormat = _configuration.PreferredPixelFormat,
            NumPlanes = 2, // NV12 typically has 2 planes
            Field = (uint)V4L2Field.NONE
        };
        captureFormat.Pix_mp = capturePixMp;

        var result = LibV4L2.SetFormat(_deviceFd, ref captureFormat);
        if (!result.Success)
        {
            _logger.LogWarning("Preferred format failed, trying alternative...");
            
            // Try alternative format
            capturePixMp.PixelFormat = _configuration.AlternativePixelFormat;
            capturePixMp.NumPlanes = 3; // YUV420 typically has 3 planes
            captureFormat.Pix_mp = capturePixMp;

            result = LibV4L2.SetFormat(_deviceFd, ref captureFormat);
            if (!result.Success)
            {
                throw new InvalidOperationException($"Failed to set capture format: {result.ErrorMessage}");
            }
        }

        _logger.LogInformation("Set capture format: {Width}x{Height}, PixelFormat: 0x{PixelFormat:X8}, Planes: {NumPlanes}",
            captureFormat.Pix_mp.Width, captureFormat.Pix_mp.Height, 
            captureFormat.Pix_mp.PixelFormat, captureFormat.Pix_mp.NumPlanes);

        await Task.CompletedTask; // Make method async for consistency
    }

    #endregion

    #region Buffer Management

    private async Task SetupBuffersAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Setting up V4L2 buffers...");

        await SetupOutputBuffersAsync(cancellationToken);
        await SetupCaptureBuffersAsync(cancellationToken);

        _logger.LogInformation("Buffer setup completed. Output: {OutputCount}, Capture: {CaptureCount}",
            _outputBufferCount, _captureBufferCount);
    }

    private async Task SetupOutputBuffersAsync(CancellationToken cancellationToken)
    {
        // Request output buffers
        var reqBufs = new V4L2RequestBuffers
        {
            Count = (uint)_configuration.OutputBufferCount,
            Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE,
            Memory = V4L2Constants.V4L2_MEMORY_MMAP
        };

        var result = LibV4L2.RequestBuffers(_deviceFd, ref reqBufs);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to request output buffers: {result.ErrorMessage}");
        }

        _outputBufferCount = reqBufs.Count;
        _logger.LogDebug("Requested {RequestedCount} output buffers, got {ActualCount}",
            _configuration.OutputBufferCount, _outputBufferCount);

        // Map and prepare output buffers
        for (uint i = 0; i < _outputBufferCount; i++)
        {
            var mappedBuffer = await MapOutputBufferAsync(i, cancellationToken);
            _outputBuffers.Add(mappedBuffer);
        }
    }

    private async Task SetupCaptureBuffersAsync(CancellationToken cancellationToken)
    {
        // Request capture buffers
        var reqBufs = new V4L2RequestBuffers
        {
            Count = (uint)_configuration.CaptureBufferCount,
            Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE,
            Memory = V4L2Constants.V4L2_MEMORY_MMAP
        };

        var result = LibV4L2.RequestBuffers(_deviceFd, ref reqBufs);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to request capture buffers: {result.ErrorMessage}");
        }

        _captureBufferCount = reqBufs.Count;
        _logger.LogDebug("Requested {RequestedCount} capture buffers, got {ActualCount}",
            _configuration.CaptureBufferCount, _captureBufferCount);

        // Map and queue capture buffers
        for (uint i = 0; i < _captureBufferCount; i++)
        {
            var mappedBuffer = await MapCaptureBufferAsync(i, cancellationToken);
            _captureBuffers.Add(mappedBuffer);

            // Queue buffer for capture
            await QueueCaptureBufferAsync(i, cancellationToken);
        }
    }

    private async Task<MappedBuffer> MapOutputBufferAsync(uint index, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var planes = new V4L2Plane[1];
            var buffer = new V4L2Buffer
            {
                Index = index,
                Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE,
                Memory = V4L2Constants.V4L2_MEMORY_MMAP,
                Length = 1
            };

            unsafe
            {
                buffer.Planes = (V4L2Plane*)Unsafe.AsPointer(ref planes[0]);
            }

            var result = LibV4L2.QueryBuffer(_deviceFd, ref buffer);
            if (!result.Success)
            {
                throw new InvalidOperationException($"Failed to query output buffer {index}: {result.ErrorMessage}");
            }

            // Map the buffer memory
            var ptr = Libc.mmap(IntPtr.Zero, planes[0].Length,
                ProtFlags.PROT_READ | ProtFlags.PROT_WRITE,
                MapFlags.MAP_SHARED, _deviceFd, planes[0].Fd);

            if (ptr == Libc.MAP_FAILED)
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"Failed to map output buffer {index}. Error: {error}");
            }

            _logger.LogDebug("Mapped output buffer {Index}: size={Size}, ptr=0x{Pointer:X8}",
                index, planes[0].Length, ptr.ToInt64());

            return new MappedBuffer
            {
                Index = index,
                Pointer = ptr,
                Size = planes[0].Length,
                Planes = planes
            };
        }, cancellationToken);
    }

    private async Task<MappedBuffer> MapCaptureBufferAsync(uint index, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var planes = new V4L2Plane[3]; // Max planes for multiplanar
            var buffer = new V4L2Buffer
            {
                Index = index,
                Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE,
                Memory = V4L2Constants.V4L2_MEMORY_MMAP,
                Length = 3
            };

            unsafe
            {
                buffer.Planes = (V4L2Plane*)Unsafe.AsPointer(ref planes[0]);
            }

            var result = LibV4L2.QueryBuffer(_deviceFd, ref buffer);
            if (!result.Success)
            {
                throw new InvalidOperationException($"Failed to query capture buffer {index}: {result.ErrorMessage}");
            }

            // Map only the first plane (or planes with non-zero length)
            IntPtr mappedPtr = IntPtr.Zero;
            uint totalSize = 0;

            for (int planeIndex = 0; planeIndex < buffer.Length; planeIndex++)
            {
                if (planes[planeIndex].Length > 0)
                {
                    if (mappedPtr == IntPtr.Zero)
                    {
                        // Map the first valid plane
                        mappedPtr = Libc.mmap(IntPtr.Zero, planes[planeIndex].Length,
                            ProtFlags.PROT_READ | ProtFlags.PROT_WRITE,
                            MapFlags.MAP_SHARED, _deviceFd, planes[planeIndex].Fd);

                        if (mappedPtr == Libc.MAP_FAILED)
                        {
                            var error = Marshal.GetLastWin32Error();
                            throw new InvalidOperationException($"Failed to map capture buffer {index}, plane {planeIndex}. Error: {error}");
                        }

                        totalSize = planes[planeIndex].Length;
                        _logger.LogDebug("Mapped capture buffer {Index}, plane {PlaneIndex}: size={Size}",
                            index, planeIndex, planes[planeIndex].Length);
                    }
                }
            }

            if (mappedPtr == IntPtr.Zero)
            {
                throw new InvalidOperationException($"No valid planes found for capture buffer {index}");
            }

            return new MappedBuffer
            {
                Index = index,
                Pointer = mappedPtr,
                Size = totalSize,
                Planes = planes
            };
        }, cancellationToken);
    }

    #endregion

    #region Streaming Operations

    private async Task StartStreamingAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting V4L2 streaming...");

        // Start output streaming
        var result = LibV4L2.StreamOn(_deviceFd, V4L2BufferType.VIDEO_OUTPUT_MPLANE);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to start output streaming: {result.ErrorMessage}");
        }

        // Start capture streaming
        result = LibV4L2.StreamOn(_deviceFd, V4L2BufferType.VIDEO_CAPTURE_MPLANE);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to start capture streaming: {result.ErrorMessage}");
        }

        _logger.LogInformation("V4L2 streaming started successfully");
        await Task.CompletedTask;
    }

    private async Task ProcessVideoFileAsync(string filePath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing video file: {FilePath}", filePath);

        var fileInfo = new FileInfo(filePath);
        long totalBytes = fileInfo.Length;
        long processedBytes = 0;
        var stopwatch = Stopwatch.StartNew();

        using var fileStream = File.OpenRead(filePath);
        var buffer = new byte[_configuration.ChunkSize];
        uint outputBufferIndex = 0;

        // Send start command to decoder
        var startResult = LibV4L2.StartDecoder(_deviceFd);
        if (!startResult.Success)
        {
            _logger.LogWarning("Start decoder command failed: {Error}", startResult.ErrorMessage);
        }

        _logger.LogInformation("Starting decode loop...");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Read data from file
            int bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (bytesRead == 0)
            {
                _logger.LogInformation("End of file reached, flushing decoder...");
                break;
            }

            processedBytes += bytesRead;

            // Queue H.264 data to decoder
            await QueueH264DataAsync(buffer, bytesRead, outputBufferIndex, cancellationToken);
            outputBufferIndex = (outputBufferIndex + 1) % _outputBufferCount;

            // Dequeue decoded frames
            await DequeueDecodedFramesAsync(cancellationToken);

            // Report progress
            ReportProgress(processedBytes, totalBytes, stopwatch.Elapsed);

            // Small delay to prevent busy waiting
            await Task.Delay(1, cancellationToken);
        }

        // Flush remaining frames
        await FlushDecoderAsync(cancellationToken);
    }

    private async Task QueueH264DataAsync(byte[] data, int length, uint bufferIndex, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            var mappedBuffer = _outputBuffers[(int)bufferIndex];

            // Copy data to mapped buffer
            Marshal.Copy(data, 0, mappedBuffer.Pointer, length);

            // Setup buffer for queuing
            mappedBuffer.Planes[0].BytesUsed = (uint)length;

            var buffer = new V4L2Buffer
            {
                Index = bufferIndex,
                Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE,
                Memory = V4L2Constants.V4L2_MEMORY_MMAP,
                Length = 1
            };

            unsafe
            {
                buffer.Planes = (V4L2Plane*)Unsafe.AsPointer(ref mappedBuffer.Planes[0]);
            }

            var result = LibV4L2.QueueBuffer(_deviceFd, ref buffer);
            if (!result.Success)
            {
                throw new InvalidOperationException($"Failed to queue output buffer {bufferIndex}: {result.ErrorMessage}");
            }

            _logger.LogTrace("Queued {ByteCount} bytes to output buffer {BufferIndex}", length, bufferIndex);
        }, cancellationToken);
    }

    private async Task DequeueDecodedFramesAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Try to dequeue a capture buffer
                var planes = new V4L2Plane[3];
                var buffer = new V4L2Buffer
                {
                    Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE,
                    Memory = V4L2Constants.V4L2_MEMORY_MMAP,
                    Length = 3
                };

                unsafe
                {
                    buffer.Planes = (V4L2Plane*)Unsafe.AsPointer(ref planes[0]);
                }

                var result = LibV4L2.DequeueBuffer(_deviceFd, ref buffer);
                if (!result.Success)
                {
                    // No more buffers available
                    break;
                }

                _framesDecoded++;
                
                // Raise frame decoded event
                OnFrameDecoded(new FrameDecodedEventArgs
                {
                    FrameNumber = _framesDecoded,
                    BufferIndex = buffer.Index,
                    BytesUsed = planes[0].BytesUsed,
                    Timestamp = DateTime.UtcNow
                });

                _logger.LogDebug("Decoded frame {FrameNumber} (buffer {BufferIndex}, {BytesUsed} bytes)",
                    _framesDecoded, buffer.Index, planes[0].BytesUsed);

                // Requeue the buffer for more captures
                QueueCaptureBufferAsync(buffer.Index, CancellationToken.None).Wait(cancellationToken);

                // Also try to dequeue processed output buffer
                DequeueOutputBuffer();
            }
        }, cancellationToken);
    }

    private async Task QueueCaptureBufferAsync(uint bufferIndex, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            var mappedBuffer = _captureBuffers[(int)bufferIndex];

            var buffer = new V4L2Buffer
            {
                Index = bufferIndex,
                Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE,
                Memory = V4L2Constants.V4L2_MEMORY_MMAP,
                Length = 3
            };

            unsafe
            {
                buffer.Planes = (V4L2Plane*)Unsafe.AsPointer(ref mappedBuffer.Planes[0]);
            }

            var result = LibV4L2.QueueBuffer(_deviceFd, ref buffer);
            if (!result.Success)
            {
                _logger.LogWarning("Failed to requeue capture buffer {BufferIndex}: {Error}",
                    bufferIndex, result.ErrorMessage);
            }
        }, cancellationToken);
    }

    private void DequeueOutputBuffer()
    {
        var outputPlanes = new V4L2Plane[1];
        var outputBuffer = new V4L2Buffer
        {
            Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE,
            Memory = V4L2Constants.V4L2_MEMORY_MMAP,
            Length = 1
        };

        unsafe
        {
            outputBuffer.Planes = (V4L2Plane*)Unsafe.AsPointer(ref outputPlanes[0]);
        }

        LibV4L2.DequeueBuffer(_deviceFd, ref outputBuffer); // Ignore result
    }

    private async Task FlushDecoderAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            var flushResult = LibV4L2.FlushDecoder(_deviceFd);
            if (!flushResult.Success)
            {
                _logger.LogWarning("Flush decoder command failed: {Error}", flushResult.ErrorMessage);
            }

            // Dequeue any remaining frames
            DequeueDecodedFramesAsync(cancellationToken).Wait(cancellationToken);
        }, cancellationToken);
    }

    #endregion

    #region Event Handling

    protected virtual void OnFrameDecoded(FrameDecodedEventArgs e)
    {
        FrameDecoded?.Invoke(this, e);
    }

    protected virtual void OnProgressChanged(DecodingProgressEventArgs e)
    {
        ProgressChanged?.Invoke(this, e);
    }

    private void ReportProgress(long processedBytes, long totalBytes, TimeSpan elapsed)
    {
        OnProgressChanged(new DecodingProgressEventArgs
        {
            BytesProcessed = processedBytes,
            TotalBytes = totalBytes,
            FramesDecoded = _framesDecoded,
            ElapsedTime = elapsed
        });
    }

    #endregion

    #region Cleanup and Disposal

    private async Task CleanupAsync()
    {
        if (_deviceFd >= 0)
        {
            _logger.LogInformation("Cleaning up decoder resources...");

            // Stop streaming
            LibV4L2.StreamOff(_deviceFd, V4L2BufferType.VIDEO_OUTPUT_MPLANE);
            LibV4L2.StreamOff(_deviceFd, V4L2BufferType.VIDEO_CAPTURE_MPLANE);

            // Stop decoder
            LibV4L2.StopDecoder(_deviceFd, true);

            // Unmap buffers
            foreach (var buffer in _outputBuffers)
            {
                if (buffer.Pointer != IntPtr.Zero)
                {
                    unsafe
                    {
                        Libc.munmap((void*)buffer.Pointer, buffer.Size);
                    }
                }
            }

            foreach (var buffer in _captureBuffers)
            {
                if (buffer.Pointer != IntPtr.Zero)
                {
                    unsafe
                    {
                        Libc.munmap((void*)buffer.Pointer, buffer.Size);
                    }
                }
            }

            // Close device
            Libc.close(_deviceFd);
            _deviceFd = -1;

            _logger.LogInformation("Decoder cleanup completed");
        }

        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CleanupAsync().Wait();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    ~H264V4L2StreamingDecoder()
    {
        Dispose();
    }

    #endregion

    #region Helper Classes

    private class MappedBuffer
    {
        public required uint Index { get; init; }
        public required IntPtr Pointer { get; init; }
        public required uint Size { get; init; }
        public required V4L2Plane[] Planes { get; init; }
    }

    #endregion
}
