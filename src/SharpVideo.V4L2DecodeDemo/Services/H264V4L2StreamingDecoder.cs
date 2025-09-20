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
    private readonly H264NaluParser _naluParser;

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
        _deviceManager = deviceManager ?? new V4L2DeviceManager(deviceLogger);

        // Initialize NALU parser
        var naluParserLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<H264NaluParser>();
        _naluParser = new H264NaluParser(naluParserLogger);
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

    /// <summary>
    /// Decodes an H.264 file using V4L2 hardware acceleration, processing data NALU by NALU
    /// </summary>
    public async Task DecodeFileNaluByNaluAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Video file not found: {filePath}");

        _logger.LogInformation("Starting H.264 NALU-by-NALU decode of {FilePath}", filePath);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Initialize decoder
            await InitializeDecoderAsync(cancellationToken);

            // Process the file NALU by NALU
            await ProcessVideoFileNaluByNaluAsync(filePath, cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation("NALU-by-NALU decoding completed successfully. {FrameCount} frames in {ElapsedTime:F2}s ({FPS:F2} fps)",
                _framesDecoded, stopwatch.Elapsed.TotalSeconds, _framesDecoded / stopwatch.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during H.264 NALU-by-NALU decoding");
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
        var devicePath = _deviceManager.FindH264DecoderDevice();
        if (string.IsNullOrEmpty(devicePath))
        {
            throw new InvalidOperationException("No suitable H.264 decoder device found");
        }

        // Open device
        _deviceFd = Libc.open(devicePath, OpenFlags.O_RDWR | OpenFlags.O_NONBLOCK);
        if (_deviceFd < 0)
        {
            var error = Marshal.GetLastWin32Error();
            string errorDescription = GetErrorDescription(error);
            _logger.LogError("Failed to open device {DevicePath}: {ErrorDesc} (Code: {ErrorCode})",
                devicePath, errorDescription, error);
            throw new InvalidOperationException($"Failed to open device {devicePath}. {errorDescription}. " +
                                               $"Check permissions and make sure the device is not already in use.");
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

        // Try multiple H.264 formats in order of likely compatibility
        uint[] h264Formats = new uint[]
        {
            0x34363248, // H264 (standard)
            0x30313748, // H710 - sometimes used for H.264 on embedded platforms
            0x34363253, // S264 (H264 Parsed Slice Data)
            0x31435641, // AVC1
            0x31637661, // avc1 (lowercase variant)
            0x48323634, // H264 (alternate byte order)
            0x30323134  // 2014 (RockChip specific H264 format)
        };

        bool formatSet = false;
        foreach (var format in h264Formats)
        {
                var outputPixMp = new V4L2PixFormatMplane
                {
                    Width = _configuration.InitialWidth,
                    Height = _configuration.InitialHeight,
                    PixelFormat = format,
                    NumPlanes = 1,
                    Field = (uint)V4L2Field.NONE,
                    // Some decoders require these fields to be properly set
                    Colorspace = 5, // V4L2_COLORSPACE_REC709
                    YcbcrEncoding = 1, // V4L2_YCBCR_ENC_DEFAULT
                    Quantization = 1, // V4L2_QUANTIZATION_DEFAULT
                    XferFunc = 1 // V4L2_XFER_FUNC_DEFAULT
                };
                outputFormat.Pix_mp = outputPixMp;            var formatResult = LibV4L2.SetFormat(_deviceFd, ref outputFormat);
            if (formatResult.Success)
            {
                _logger.LogInformation("Successfully set H.264 format: 0x{Format:X8}", format);
                formatSet = true;
                break;
            }
            else
            {
                int errorCode = Marshal.GetLastWin32Error();
                string errorDesc = GetErrorDescription(errorCode);
                _logger.LogDebug("Format 0x{Format:X8} not supported: {Error} - {ErrorDesc}",
                    format, formatResult.ErrorMessage, errorDesc);
            }
        }

        // If we couldn't set a format, throw an exception
        if (!formatSet)
        {
            _logger.LogError("Failed to set any supported H.264 format on the device");
            throw new InvalidOperationException(
                "Failed to set any supported H.264 format on the device. " +
                "This may indicate that the hardware decoder doesn't support standard H.264 formats, " +
                "or requires special configuration not currently implemented.");
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

        // For some V4L2 decoders, especially RockChip ones, we need to send smaller chunks
        // with very clear NAL boundaries to avoid EBADR errors
        int smallChunkSize = 1024; // Try smaller chunks
        bool firstFrameSent = false;

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

            // For the first buffer, try special handling
            if (!firstFrameSent)
            {
                _logger.LogDebug("Processing first frame specially");

                // Look for SPS+PPS NAL units to send as first frame
                // Process H.264 data to ensure proper NAL units
                byte[] processedBuffer = ProcessH264Data(buffer, bytesRead);

                // Queue the processed H.264 data to decoder
                if (processedBuffer.Length > 0)
                {
                    // First frame is likely to contain SPS/PPS, mark it as a keyframe
                    await QueueH264DataAsync(processedBuffer, processedBuffer.Length, outputBufferIndex, cancellationToken, isKeyFrame: true);
                    outputBufferIndex = (outputBufferIndex + 1) % _outputBufferCount;
                    firstFrameSent = true;
                }
            }
            else
            {
                // For subsequent frames, break into smaller chunks at NAL boundaries
                _logger.LogTrace("Processing subsequent frame in smaller chunks");

                // Split into smaller chunks for better compatibility
                int position = 0;
                while (position < bytesRead)
                {
                    // Determine chunk size, but try to find NAL boundary
                    int endPos = Math.Min(position + smallChunkSize, bytesRead);

                    // If we're not at the end, try to find a NAL boundary
                    if (endPos < bytesRead)
                    {
                        // Look for next NAL start code after our minimum position
                        for (int i = position + 4; i < Math.Min(bytesRead - 4, position + smallChunkSize * 2); i++)
                        {
                            if ((buffer[i] == 0 && buffer[i+1] == 0 && buffer[i+2] == 1) ||
                                (buffer[i] == 0 && buffer[i+1] == 0 && buffer[i+2] == 0 && buffer[i+3] == 1))
                            {
                                // Found a NAL start code, break here
                                endPos = i;
                                break;
                            }
                        }
                    }

                    // Process this chunk
                    int chunkSize = endPos - position;
                    if (chunkSize > 0)
                    {
                        byte[] chunk = new byte[chunkSize];
                        Array.Copy(buffer, position, chunk, 0, chunkSize);

                        byte[] processedChunk = ProcessH264Data(chunk, chunkSize);

                        if (processedChunk.Length > 0)
                        {
                            // Check if this chunk might contain an SPS (keyframe)
                            bool isChunkWithSps = false;
                            for (int i = 0; i < chunk.Length - 5; i++) {
                                if (chunk[i] == 0 && chunk[i+1] == 0 &&
                                    ((chunk[i+2] == 1) || (chunk[i+2] == 0 && chunk[i+3] == 1))) {
                                    int nalHeaderPos = chunk[i+2] == 0 ? i+4 : i+3;
                                    if (nalHeaderPos < chunk.Length) {
                                        byte nalType = (byte)(chunk[nalHeaderPos] & 0x1F);
                                        if (nalType == 7) { // SPS
                                            isChunkWithSps = true;
                                            break;
                                        }
                                    }
                                }
                            }

                            await QueueH264DataAsync(processedChunk, processedChunk.Length, outputBufferIndex, cancellationToken, isKeyFrame: isChunkWithSps);
                            outputBufferIndex = (outputBufferIndex + 1) % _outputBufferCount;
                        }
                    }

                    position = endPos;
                }
            }

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

    private async Task ProcessVideoFileNaluByNaluAsync(string filePath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing video file NALU by NALU: {FilePath}", filePath);

        var fileInfo = new FileInfo(filePath);
        long totalBytes = fileInfo.Length;
        long processedBytes = 0;
        var stopwatch = Stopwatch.StartNew();
        uint outputBufferIndex = 0;
        int naluCount = 0;

        // Send start command to decoder
        var startResult = LibV4L2.StartDecoder(_deviceFd);
        if (!startResult.Success)
        {
            _logger.LogWarning("Start decoder command failed: {Error}", startResult.ErrorMessage);
        }

        _logger.LogInformation("Starting NALU-by-NALU decode loop...");

        using var fileStream = File.OpenRead(filePath);

        // Process NALUs one by one
        await foreach (var nalu in _naluParser.ParseNalusFromStreamAsync(fileStream, cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            naluCount++;

            // Log detailed information about important NALUs
            if (nalu.Type == 7 || nalu.Type == 8 || nalu.Type == 5)
            {
                _logger.LogDebug("Processing NALU {Count}: Type={Type} ({Description}), Size={Size} bytes, KeyFrame={IsKeyFrame}",
                    naluCount, nalu.Type, nalu.TypeDescription, nalu.Data.Length, nalu.IsKeyFrame);
            }
            else
            {
                _logger.LogTrace("Processing NALU {Count}: Type={Type} ({Description}), Size={Size} bytes",
                    naluCount, nalu.Type, nalu.TypeDescription, nalu.Data.Length);
            }

            // Validate the NALU before processing
            if (!_naluParser.ValidateNalu(nalu))
            {
                _logger.LogWarning("Skipping invalid NALU {Count}", naluCount);
                continue;
            }

            // Queue the NALU to the decoder
            try
            {
                await QueueNaluAsync(nalu, outputBufferIndex, cancellationToken);
                outputBufferIndex = (outputBufferIndex + 1) % _outputBufferCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to queue NALU {Count} (Type={Type})", naluCount, nalu.Type);

                // For critical NALUs (SPS, PPS, IDR), re-throw the exception
                if (nalu.Type == 7 || nalu.Type == 8 || nalu.Type == 5)
                {
                    throw;
                }

                // For other NALUs, continue processing
                _logger.LogWarning("Continuing with next NALU after error with NALU {Count}", naluCount);
            }

            // Dequeue decoded frames
            await DequeueDecodedFramesAsync(cancellationToken);

            // Update progress tracking
            processedBytes += nalu.Data.Length;
            ReportProgress(processedBytes, totalBytes, stopwatch.Elapsed);

            // Small delay to prevent overwhelming the decoder
            if (naluCount % 10 == 0)
            {
                await Task.Delay(1, cancellationToken);
            }
        }

        _logger.LogInformation("Processed {NaluCount} NALUs from {FilePath}", naluCount, filePath);

        // Flush remaining frames
        await FlushDecoderAsync(cancellationToken);
    }

    /// <summary>
    /// Queues a single NALU to the hardware decoder
    /// </summary>
    private async Task QueueNaluAsync(H264Nalu nalu, uint bufferIndex, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            try
            {
                var mappedBuffer = _outputBuffers[(int)bufferIndex];

                // Safety check for buffer size
                if (nalu.Data.Length > mappedBuffer.Size)
                {
                    _logger.LogWarning("NALU ({NaluSize} bytes) exceeds buffer size ({BufferSize} bytes), truncating",
                        nalu.Data.Length, mappedBuffer.Size);

                    // For critical NALUs, this is an error
                    if (nalu.IsKeyFrame)
                    {
                        throw new InvalidOperationException($"Critical NALU (type {nalu.Type}) too large for buffer");
                    }

                    // For non-critical NALUs, skip them
                    _logger.LogWarning("Skipping NALU type {Type} due to size constraints", nalu.Type);
                    return;
                }

                // Copy NALU data to mapped buffer
                Marshal.Copy(nalu.Data, 0, mappedBuffer.Pointer, nalu.Data.Length);

                // Setup buffer for queuing
                mappedBuffer.Planes[0].BytesUsed = (uint)nalu.Data.Length;

                // Set appropriate flags
                uint flags = 0x01; // V4L2_BUF_FLAG_MAPPED

                if (nalu.IsKeyFrame)
                {
                    flags |= 0x00000008; // V4L2_BUF_FLAG_KEYFRAME
                    _logger.LogDebug("Setting KEYFRAME flag for NALU type {Type}", nalu.Type);
                }

                // Create buffer structure
                var buffer = new V4L2Buffer
                {
                    Index = bufferIndex,
                    Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE,
                    Memory = V4L2Constants.V4L2_MEMORY_MMAP,
                    Length = 1,
                    Field = (uint)V4L2Field.NONE,
                    BytesUsed = (uint)nalu.Data.Length,
                    Flags = flags,
                    Timestamp = new TimeVal { TvSec = 0, TvUsec = 0 },
                    Sequence = 0
                };

                unsafe
                {
                    buffer.Planes = (V4L2Plane*)Unsafe.AsPointer(ref mappedBuffer.Planes[0]);
                }

                // Queue the buffer
                var result = LibV4L2.QueueBuffer(_deviceFd, ref buffer);
                if (!result.Success)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    string errorDescription = GetErrorDescription(errorCode);
                    string v4l2Context = GetV4L2ErrorContext(errorCode) ?? string.Empty;

                    _logger.LogError("Failed to queue NALU buffer {Index} (Type={NaluType}): {ErrorDesc} - {Context}",
                        bufferIndex, nalu.Type, errorDescription, v4l2Context);

                    throw new InvalidOperationException(
                        $"Failed to queue NALU buffer {bufferIndex} (Type={nalu.Type}): {errorDescription}. {v4l2Context}");
                }

                _logger.LogTrace("Queued NALU type {Type} ({Size} bytes) to buffer {BufferIndex}",
                    nalu.Type, nalu.Data.Length, bufferIndex);
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                _logger.LogError(ex, "Unexpected error queuing NALU buffer {BufferIndex}", bufferIndex);
                throw new InvalidOperationException($"Failed to queue NALU buffer {bufferIndex}: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    private async Task QueueH264DataAsync(byte[] data, int length, uint bufferIndex, CancellationToken cancellationToken, bool isKeyFrame = false, bool isEos = false)
    {
        await Task.Run(() =>
        {
            try
            {
                // Skip empty data
                if (length == 0)
                {
                    _logger.LogDebug("Skipping empty data buffer");
                    return;
                }

                var mappedBuffer = _outputBuffers[(int)bufferIndex];

                // Safety check for buffer size
                if (length > mappedBuffer.Size)
                {
                    _logger.LogWarning("Data ({DataSize} bytes) exceeds buffer size ({BufferSize} bytes), truncating",
                        length, mappedBuffer.Size);
                    length = (int)mappedBuffer.Size;
                }

                // Log the first few bytes of the data for debugging
                if (length > 16)
                {
                    byte[] firstBytes = new byte[16];
                    Array.Copy(data, firstBytes, 16);
                    _logger.LogDebug("Buffer header: {Bytes}", BitConverter.ToString(firstBytes));
                }

                // Copy data to mapped buffer
                Marshal.Copy(data, 0, mappedBuffer.Pointer, length);

                // Setup buffer for queuing - BytesUsed is critical
                mappedBuffer.Planes[0].BytesUsed = (uint)length;

                // Check for SPS NAL unit (which is always part of a keyframe)
                bool isKeyFrame = false;
                if (length > 4)
                {
                    // Check for NAL unit type 7 (SPS) which indicates a keyframe
                    for (int i = 0; i < length - 4; i++)
                    {
                        if (data[i] == 0 && data[i + 1] == 0 &&
                            ((data[i + 2] == 0 && data[i + 3] == 1) || (data[i + 2] == 1)))
                        {
                            int nalHeaderPos = data[i + 2] == 0 ? i + 4 : i + 3;
                            if (nalHeaderPos < length)
                            {
                                byte nalType = (byte)(data[nalHeaderPos] & 0x1F);
                                if (nalType == 7) // SPS NAL unit
                                {
                                    isKeyFrame = true;
                                    _logger.LogDebug("Found SPS NAL unit, marking as keyframe");
                                    break;
                                }
                            }
                        }
                    }
                }

                // Set appropriate flags for RockChip decoder
                uint flags = 0;

                // V4L2_BUF_FLAG_KEYFRAME = 0x00000008
                if (isKeyFrame)
                {
                    flags |= 0x00000008; // V4L2_BUF_FLAG_KEYFRAME
                    _logger.LogDebug("Setting KEYFRAME flag");
                }

                // For RockChip decoder specifically, set additional flags:
                // - 0x01 = V4L2_BUF_FLAG_MAPPED (buffer has been mapped)
                flags |= 0x01;  // V4L2_BUF_FLAG_MAPPED

                // End-of-stream flag if needed
                if (isEos)
                {
                    flags |= 0x00100000; // V4L2_BUF_FLAG_LAST
                    _logger.LogDebug("Setting EOS flag");
                }

                // Create specialized buffer for RockChip decoder
                var buffer = new V4L2Buffer
                {
                    Index = bufferIndex,
                    Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE,
                    Memory = V4L2Constants.V4L2_MEMORY_MMAP,
                    Length = 1,
                    Field = (uint)V4L2Field.NONE,
                    BytesUsed = (uint)length, // Critical field - must match actual data size
                    Flags = flags,
                    // Set timestamp to 0
                    Timestamp = new TimeVal { TvSec = 0, TvUsec = 0 },
                    Sequence = 0  // Incremental frame counter could help
                };

                unsafe
                {
                    buffer.Planes = (V4L2Plane*)Unsafe.AsPointer(ref mappedBuffer.Planes[0]);
                }

                // Check if the device is in a valid state
                if (_deviceFd < 0)
                {
                    throw new InvalidOperationException("Device is not properly initialized");
                }

                // Log buffer details before queueing
                _logger.LogDebug("Queueing buffer {Index}: Type={Type}, Memory={Memory}, BytesUsed={Bytes}, Planes={PlaneCount}",
                    buffer.Index, buffer.Type, buffer.Memory, buffer.BytesUsed, buffer.Length);

                // Try to check first few bytes to see what we're sending
                try {
                    unsafe {
                        byte[] headerBytes = new byte[Math.Min(16, length)];
                        Marshal.Copy(mappedBuffer.Pointer, headerBytes, 0, headerBytes.Length);
                        string firstBytes = BitConverter.ToString(headerBytes);
                        _logger.LogDebug("First bytes of buffer {Index}: {Bytes}", bufferIndex, firstBytes);
                    }
                } catch (Exception ex) {
                    _logger.LogDebug("Could not log buffer content: {Message}", ex.Message);
                }

                var result = LibV4L2.QueueBuffer(_deviceFd, ref buffer);
                if (!result.Success)
                {
                    // Get the exact error code for diagnostics
                    int errorCode = Marshal.GetLastWin32Error();

                    // Get general and V4L2-specific error descriptions
                    string errorDescription = GetErrorDescription(errorCode);
                    string v4l2Context = GetV4L2ErrorContext(errorCode) ?? string.Empty;

                    _logger.LogError("V4L2 error: QueueBuffer failed with code {ErrorCode} - {ErrorDesc}",
                        errorCode, errorDescription);

                    // Try to get more diagnostic info about the format
                    try {
                        V4L2Format fmt = new V4L2Format();
                        fmt.Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE;
                        var fmtResult = LibV4L2.GetFormat(_deviceFd, ref fmt);
                        if (fmtResult.Success) {
                            _logger.LogError("Current output format: Width={Width}, Height={Height}, PixelFormat=0x{PixelFormat:X8}",
                                fmt.Pix_mp.Width, fmt.Pix_mp.Height, fmt.Pix_mp.PixelFormat);
                        }
                    } catch {
                        // Ignore errors during diagnostic info gathering
                    }

                    throw new InvalidOperationException(
                        $"Failed to queue output buffer {bufferIndex}: {errorDescription}. {v4l2Context}");
                }

                _logger.LogTrace("Queued {ByteCount} bytes to output buffer {BufferIndex}", length, bufferIndex);
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                _logger.LogError(ex, "Unexpected error queuing buffer {BufferIndex}", bufferIndex);
                throw new InvalidOperationException($"Failed to queue output buffer {bufferIndex}: {ex.Message}", ex);
            }
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
                    // EAGAIN (11) is not an error, it just means no buffers are ready
                    int errorCode = Marshal.GetLastWin32Error();
                    if (errorCode == 11) // EAGAIN
                    {
                        // No more buffers available, this is normal
                        break;
                    }

                    // For other errors, log them but continue operation
                    string errorDescription = GetErrorDescription(errorCode);
                    _logger.LogWarning("DequeueBuffer failed: {ErrorDesc} (Code: {ErrorCode})",
                        errorDescription, errorCode);
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
            // Skip if we're already disposed or the device is closed
            if (_disposed || _deviceFd < 0)
            {
                _logger.LogDebug("Skipping buffer queue on closed device");
                return;
            }

            try
            {
                var mappedBuffer = _captureBuffers[(int)bufferIndex];

                var buffer = new V4L2Buffer
                {
                    Index = bufferIndex,
                    Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE,
                    Memory = V4L2Constants.V4L2_MEMORY_MMAP,
                    Length = 3,
                    // Add flags as needed
                    Flags = 0
                };

                unsafe
                {
                    buffer.Planes = (V4L2Plane*)Unsafe.AsPointer(ref mappedBuffer.Planes[0]);
                }

                var result = LibV4L2.QueueBuffer(_deviceFd, ref buffer);
                if (!result.Success)
                {
                    // Get the exact error code for better diagnostics
                    int errorCode = Marshal.GetLastWin32Error();
                    string errorDescription = GetErrorDescription(errorCode);
                    string v4l2Context = GetV4L2ErrorContext(errorCode);

                    _logger.LogWarning("Failed to queue capture buffer {BufferIndex}: {Error} - {ErrorDesc}. {Context}",
                        bufferIndex, result.ErrorMessage, errorDescription, v4l2Context);

                    // Don't throw for capture buffer errors, just log them
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while queuing capture buffer {BufferIndex}", bufferIndex);
                // Don't throw and interrupt the decoding process
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

    #region H.264 Processing

    /// <summary>
    /// Processes H.264 data to ensure it has valid NAL units for the V4L2 decoder
    /// </summary>
    private byte[] ProcessH264Data(byte[] buffer, int length)
    {
        // Early exit for empty buffers
        if (length <= 4)
        {
            _logger.LogWarning("Buffer too small for H.264 data: {Length} bytes", length);
            return new byte[0]; // Return empty to skip this chunk
        }

        try
        {
            using var ms = new MemoryStream();

            // ROCKCHIP SPECIFIC APPROACH - V4 (trying a completely different approach):
            // The RockChip V4L2 decoder requires specially formatted input:
            // 1. It may need a complete H.264 frame with no padding
            // 2. It requires complete NAL units (no partial NAL units)
            // 3. EBADR (Invalid request descriptor) often means format incompatibility

            // Let's try a much simpler approach first - just ensure we have valid start codes
            // and send the minimal amount of data needed:

            // First, detect if this buffer contains an SPS NAL unit
            bool hasSps = false;
            bool hasPps = false;
            List<(int start, int end, byte nalType)> nalUnits = new List<(int, int, byte)>();

            // Scan for NAL units and their types
            int i = 0;
            while (i < length - 2) // Need at least 3 bytes for a start code
            {
                // Check for potential start code
                if (i + 1 < length && buffer[i] == 0 && buffer[i+1] == 0)
                {
                    // Check for 3-byte or 4-byte start code
                    bool is4ByteStartCode = (i+3 < length && buffer[i+2] == 0 && buffer[i+3] == 1);
                    bool is3ByteStartCode = (i+2 < length && buffer[i+2] == 1);

                    if (is4ByteStartCode || is3ByteStartCode)
                    {
                        int startCodeSize = is4ByteStartCode ? 4 : 3;
                        int nalStart = i + startCodeSize;

                        // Find the type of this NAL unit - ensure we have at least one byte for NAL header
                        if (nalStart < length)
                        {
                            byte nalType = (byte)(buffer[nalStart] & 0x1F); // Extract NAL type bits

                            // Find the end of this NAL (next start code or end of buffer)
                            int nalEnd = length;
                            for (int j = nalStart + 1; j < length - 2; j++) // Need at least 3 bytes for a start code
                            {
                                if (j + 1 < length && buffer[j] == 0 && buffer[j+1] == 0)
                                {
                                    bool nextIs4ByteStartCode = (j+3 < length && buffer[j+2] == 0 && buffer[j+3] == 1);
                                    bool nextIs3ByteStartCode = (j+2 < length && buffer[j+2] == 1);

                                    if (nextIs4ByteStartCode || nextIs3ByteStartCode)
                                    {
                                        nalEnd = j;
                                        break;
                                    }
                                }
                            }

                            // Add this NAL unit to our list
                            nalUnits.Add((nalStart, nalEnd, nalType)); // Store actual data start after start code

                            // Track if we have SPS/PPS NALs
                            if (nalType == 7) hasSps = true;
                            if (nalType == 8) hasPps = true;

                            // Skip to the end of this NAL
                            i = nalEnd;
                        }
                        else
                        {
                            i += startCodeSize;
                        }
                    }
                    else
                    {
                        i++;
                    }
                }
                else
                {
                    i++;
                }
            }

            // Now process NAL units based on what we found
            if (nalUnits.Count == 0)
            {
                // No valid NAL units found, just add a start code and pass through
                _logger.LogWarning("No valid NAL units found in buffer, adding start code");
                ms.Write(new byte[] { 0, 0, 0, 1 }, 0, 4);
                ms.Write(buffer, 0, length);
            }
            else
            {
                // Process NAL units
                _logger.LogDebug($"Found {nalUnits.Count} NAL units. SPS: {hasSps}, PPS: {hasPps}");

                // Special treatment for the first frame with SPS/PPS
                if (hasSps || hasPps)
                {
                    // For RockChip, it's critical to put SPS and PPS in the correct order
                    foreach (var nal in nalUnits)
                    {
                        // Always use 4-byte start codes
                        ms.Write(new byte[] { 0, 0, 0, 1 }, 0, 4);

                        // Write the NAL data - Calculate offset and length carefully to avoid index out of bounds
                        int startOffset = nal.start;

                        // Make sure we don't go out of bounds
                        if (startOffset < length && nal.end <= length)
                        {
                            int dataLength = nal.end - startOffset;
                            ms.Write(buffer, startOffset, dataLength);
                        }
                    }
                }
                else
                {
                    // For non-SPS/PPS frames, just use standard 4-byte start codes
                    foreach (var nal in nalUnits)
                    {
                        // Always use 4-byte start codes
                        ms.Write(new byte[] { 0, 0, 0, 1 }, 0, 4);

                        // Write the NAL data - Calculate offset and length carefully to avoid index out of bounds
                        int startOffset = nal.start;

                        // Make sure we don't go out of bounds
                        if (startOffset < length && nal.end <= length)
                        {
                            int dataLength = nal.end - startOffset;
                            ms.Write(buffer, startOffset, dataLength);
                        }
                    }
                }
            }

            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing H.264 data: {Message}", ex.Message);
            // In case of error, still try to send something sane
            return buffer.Take(length).ToArray();
        }
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Converts Linux error codes to descriptive error messages
    /// </summary>
    /// <param name="errorCode">The numeric error code</param>
    /// <returns>A descriptive error message</returns>
    private string GetErrorDescription(int errorCode)
    {
        switch (errorCode)
        {
            case 1: return "EPERM: Operation not permitted";
            case 2: return "ENOENT: No such file or directory";
            case 3: return "ESRCH: No such process";
            case 4: return "EINTR: Interrupted system call";
            case 5: return "EIO: Input/output error";
            case 6: return "ENXIO: No such device or address";
            case 7: return "E2BIG: Argument list too long";
            case 8: return "ENOEXEC: Exec format error";
            case 9: return "EBADF: Bad file descriptor";
            case 10: return "ECHILD: No child processes";
            case 11: return "EAGAIN: Resource temporarily unavailable (try again)";
            case 12: return "ENOMEM: Cannot allocate memory";
            case 13: return "EACCES: Permission denied";
            case 14: return "EFAULT: Bad address";
            case 15: return "ENOTBLK: Block device required";
            case 16: return "EBUSY: Device or resource busy";
            case 17: return "EEXIST: File exists";
            case 18: return "EXDEV: Invalid cross-device link";
            case 19: return "ENODEV: No such device";
            case 20: return "ENOTDIR: Not a directory";
            case 21: return "EISDIR: Is a directory";
            case 22: return "EINVAL: Invalid argument";
            case 23: return "ENFILE: Too many open files in system";
            case 24: return "EMFILE: Too many open files";
            case 25: return "ENOTTY: Inappropriate ioctl for device";
            case 26: return "ETXTBSY: Text file busy";
            case 27: return "EFBIG: File too large";
            case 28: return "ENOSPC: No space left on device";
            case 29: return "ESPIPE: Illegal seek";
            case 30: return "EROFS: Read-only file system";
            case 31: return "EMLINK: Too many links";
            case 32: return "EPIPE: Broken pipe";
            case 33: return "EDOM: Numerical argument out of domain";
            case 34: return "ERANGE: Numerical result out of range";
            case 35: return "EDEADLK: Resource deadlock avoided";
            case 36: return "ENAMETOOLONG: File name too long";
            case 37: return "ENOLCK: No locks available";
            case 38: return "ENOSYS: Function not implemented";
            case 39: return "ENOTEMPTY: Directory not empty";
            case 40: return "ELOOP: Too many levels of symbolic links";
            case 42: return "ENOMSG: No message of desired type";
            case 43: return "EIDRM: Identifier removed";
            case 44: return "ECHRNG: Channel number out of range";
            case 45: return "EL2NSYNC: Level 2 not synchronized";
            case 46: return "EL3HLT: Level 3 halted";
            case 47: return "EL3RST: Level 3 reset";
            case 48: return "ELNRNG: Link number out of range";
            case 49: return "EUNATCH: Protocol driver not attached";
            case 50: return "ENOCSI: No CSI structure available";
            case 51: return "EL2HLT: Level 2 halted";
            case 52: return "EBADE: Invalid exchange";
            case 53: return "EBADR: Invalid request descriptor";
            case 54: return "EXFULL: Exchange full";
            case 55: return "ENOANO: No anode";
            case 56: return "EBADRQC: Invalid request code";
            case 57: return "EBADSLT: Invalid slot";
            case 59: return "EBFONT: Bad font file format";
            case 60: return "ENOSTR: Device not a stream";
            case 61: return "ENODATA: No data available";
            case 62: return "ETIME: Timer expired";
            case 63: return "ENOSR: Out of streams resources";
            case 64: return "ENONET: Machine is not on the network";
            case 65: return "ENOPKG: Package not installed";
            case 66: return "EREMOTE: Object is remote";
            case 67: return "ENOLINK: Link has been severed";
            case 68: return "EADV: Advertise error";
            case 69: return "ESRMNT: Srmount error";
            case 70: return "ECOMM: Communication error on send";
            case 71: return "EPROTO: Protocol error";
            case 72: return "EMULTIHOP: Multihop attempted";
            case 73: return "EDOTDOT: RFS specific error";
            case 74: return "EBADMSG: Bad message";
            case 75: return "EOVERFLOW: Value too large for defined data type";
            case 76: return "ENOTUNIQ: Name not unique on network";
            case 77: return "EBADFD: File descriptor in bad state";
            case 78: return "EREMCHG: Remote address changed";
            case 79: return "ELIBACC: Can not access a needed shared library";
            case 80: return "ELIBBAD: Accessing a corrupted shared library";
            case 81: return "ELIBSCN: .lib section in a.out corrupted";
            case 82: return "ELIBMAX: Attempting to link in too many shared libraries";
            case 83: return "ELIBEXEC: Cannot exec a shared library directly";
            case 84: return "EILSEQ: Invalid or incomplete multibyte or wide character";
            case 85: return "ERESTART: Interrupted system call should be restarted";
            case 86: return "ESTRPIPE: Streams pipe error";
            case 87: return "EUSERS: Too many users";
            case 88: return "ENOTSOCK: Socket operation on non-socket";
            case 89: return "EDESTADDRREQ: Destination address required";
            case 90: return "EMSGSIZE: Message too long";
            case 91: return "EPROTOTYPE: Protocol wrong type for socket";
            case 92: return "ENOPROTOOPT: Protocol not available";
            case 93: return "EPROTONOSUPPORT: Protocol not supported";
            case 94: return "ESOCKTNOSUPPORT: Socket type not supported";
            case 95: return "EOPNOTSUPP: Operation not supported";
            case 96: return "EPFNOSUPPORT: Protocol family not supported";
            case 97: return "EAFNOSUPPORT: Address family not supported by protocol";
            case 98: return "EADDRINUSE: Address already in use";
            case 99: return "EADDRNOTAVAIL: Cannot assign requested address";
            case 100: return "ENETDOWN: Network is down";
            case 101: return "ENETUNREACH: Network is unreachable";
            case 102: return "ENETRESET: Network dropped connection on reset";
            case 103: return "ECONNABORTED: Software caused connection abort";
            case 104: return "ECONNRESET: Connection reset by peer";
            case 105: return "ENOBUFS: No buffer space available";
            case 106: return "EISCONN: Transport endpoint is already connected";
            case 107: return "ENOTCONN: Transport endpoint is not connected";
            case 108: return "ESHUTDOWN: Cannot send after transport endpoint shutdown";
            case 109: return "ETOOMANYREFS: Too many references: cannot splice";
            case 110: return "ETIMEDOUT: Connection timed out";
            case 111: return "ECONNREFUSED: Connection refused";
            case 112: return "EHOSTDOWN: Host is down";
            case 113: return "EHOSTUNREACH: No route to host";
            case 114: return "EALREADY: Operation already in progress";
            case 115: return "EINPROGRESS: Operation now in progress";
            case 116: return "ESTALE: Stale file handle";
            case 117: return "EUCLEAN: Structure needs cleaning";
            case 118: return "ENOTNAM: Not a XENIX named type file";
            case 119: return "ENAVAIL: No XENIX semaphores available";
            case 120: return "EISNAM: Is a named type file";
            case 121: return "EREMOTEIO: Remote I/O error";
            case 122: return "EDQUOT: Disk quota exceeded";
            default: return $"Unknown error code: {errorCode}";
        }
    }

    /// <summary>
    /// Gets detailed V4L2-specific information for an error code
    /// </summary>
    private string GetV4L2ErrorContext(int errorCode)
    {
        switch (errorCode)
        {
            case 22: // EINVAL
                return "Invalid argument - This typically indicates an incorrect parameter was passed to the V4L2 driver, " +
                       "such as an unsupported format or invalid buffer configuration.";

            case 53: // EBADR
                return "Invalid request descriptor - In V4L2 context, this often means the data format is not what the driver expects. " +
                       "Check if the H.264 stream is properly formatted with valid NAL units, or if it needs specific pre-processing " +
                       "for this hardware decoder.";

            case 74: // EBADMSG
                return "Bad message - This indicates the input data is incorrectly formatted or corrupted. " +
                       "The hardware decoder cannot parse the provided H.264 stream.";

            case 5: // EIO
                return "Input/output error - The hardware decoder encountered a problem during operation. " +
                       "This could be due to hardware limitations, driver bugs, or invalid stream data.";

            case 11: // EAGAIN
                return "Resource temporarily unavailable - The operation would block because resources are not available. " +
                       "Try again later or check if too many concurrent operations are happening.";

            default:
                return string.Empty; // No specific V4L2 context information available
        }
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

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await CleanupAsync();
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
