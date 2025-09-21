using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.H264;
using SharpVideo.Linux.Native;
using SharpVideo.V4L2;
using SharpVideo.V4L2DecodeDemo.Interfaces;
using SharpVideo.V4L2DecodeDemo.Models;
using SharpVideo.V4L2DecodeDemo.Services.Stateless;

namespace SharpVideo.V4L2DecodeDemo.Services;

/// <summary>
/// Modern, clean H.264 decoder using V4L2 hardware acceleration for stateless decoders.
///
/// This implementation correctly separates bitstream data from metadata according to V4L2 specifications:
/// - SPS/PPS parameter sets are sent as V4L2 controls
/// - Slice headers are parsed and sent as V4L2 controls
/// - Only slice data (without parameter sets) is sent in OUTPUT buffers
///
/// The architecture is modular with separate components for:
/// - Parameter set parsing (IH264ParameterSetParser)
/// - Control management (IV4L2StatelessControlManager)
/// - Slice processing (IStatelessSliceProcessor)
/// </summary>
[SupportedOSPlatform("linux")]
public class H264V4L2StatelessDecoder
{
    private readonly V4L2Device _device;
    private readonly ILogger<H264V4L2StatelessDecoder> _logger;
    private readonly DecoderConfiguration _configuration;
    private readonly IH264ParameterSetParser _parameterSetParser;
    private readonly IV4L2StatelessControlManager _controlManager;
    private readonly IStatelessSliceProcessor _sliceProcessor;

    private readonly List<MappedBuffer> _outputBuffers = new();
    private readonly List<MappedBuffer> _captureBuffers = new();
    private bool _disposed;
    private readonly uint _outputBufferCount = 4;
    private readonly uint _captureBufferCount = 4;
    private int _framesDecoded;
    private readonly Stopwatch _decodingStopwatch = new();

    private bool _hasValidParameterSets;
    private bool _isInitialized;

    public event EventHandler<FrameDecodedEventArgs>? FrameDecoded;
    public event EventHandler<DecodingProgressEventArgs>? ProgressChanged;

    public H264V4L2StatelessDecoder(
        V4L2Device device,
        ILogger<H264V4L2StatelessDecoder> logger,
        DecoderConfiguration? configuration = null,
        IH264ParameterSetParser? parameterSetParser = null,
        IV4L2StatelessControlManager? controlManager = null,
        IStatelessSliceProcessor? sliceProcessor = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? new DecoderConfiguration();

        // Create dependencies with proper logger factory
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        
        _parameterSetParser = parameterSetParser ?? new H264ParameterSetParser(
            loggerFactory.CreateLogger<H264ParameterSetParser>(), 
            _configuration.InitialWidth, 
            _configuration.InitialHeight);

        _controlManager = controlManager ?? new V4L2StatelessControlManager(
            loggerFactory.CreateLogger<V4L2StatelessControlManager>(), 
            _parameterSetParser, 
            _device);

        _sliceProcessor = sliceProcessor ?? new StatelessSliceProcessor(
            loggerFactory.CreateLogger<StatelessSliceProcessor>(), 
            _controlManager, 
            _parameterSetParser, 
            _device, 
            _outputBuffers, 
            () => _hasValidParameterSets);
    }

    /// <summary>
    /// Decodes an H.264 file using V4L2 hardware acceleration (stateless decoder)
    /// </summary>
    public async Task DecodeFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Video file not found: {filePath}");

        _logger.LogInformation("Starting H.264 stateless decode of {FilePath}", filePath);
        _decodingStopwatch.Start();

        try
        {
            // Initialize decoder and dependencies
            await InitializeDecoderAsync(cancellationToken);

            // Extract and set parameter sets
            await ExtractAndSetParameterSetsAsync(filePath, cancellationToken);

            // Process the file with stateless decoding
            await ProcessVideoFileStatelessAsync(filePath, cancellationToken);

            _decodingStopwatch.Stop();
            _logger.LogInformation("Stateless decoding completed successfully. {FrameCount} frames in {ElapsedTime:F2}s ({FPS:F2} fps)",
                _framesDecoded, _decodingStopwatch.Elapsed.TotalSeconds, _framesDecoded / _decodingStopwatch.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during H.264 stateless decoding");
            throw;
        }
        finally
        {
            Cleanup();
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

        _logger.LogInformation("Starting H.264 stateless NALU-by-NALU decode of {FilePath}", filePath);
        _decodingStopwatch.Start();

        try
        {
            // Initialize decoder and dependencies
            await InitializeDecoderAsync(cancellationToken);

            // Extract and set parameter sets
            await ExtractAndSetParameterSetsAsync(filePath, cancellationToken);

            // Process the file NALU by NALU with stateless decoding
            await ProcessVideoFileNaluByNaluStatelessAsync(filePath, cancellationToken);

            _decodingStopwatch.Stop();
            _logger.LogInformation("Stateless NALU-by-NALU decoding completed successfully. {FrameCount} frames in {ElapsedTime:F2}s ({FPS:F2} fps)",
                _framesDecoded, _decodingStopwatch.Elapsed.TotalSeconds, _framesDecoded / _decodingStopwatch.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during H.264 stateless NALU-by-NALU decoding");
            throw;
        }
        finally
        {
            Cleanup();
        }
    }

    private async Task InitializeDecoderAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
            return;

        _logger.LogInformation("Initializing H.264 stateless decoder...");

        try
        {
            // Configure decoder formats
            ConfigureFormats();
            
            // Configure V4L2 controls for stateless operation
            await _controlManager.ConfigureStatelessControlsAsync(cancellationToken);
            
            // Setup and map buffers properly
            await SetupAndMapBuffersAsync();
            
            // Start streaming on both queues
            StartStreaming();

            _isInitialized = true;
            _logger.LogInformation("Decoder initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize decoder");
            throw new InvalidOperationException($"Decoder initialization failed: {ex.Message}", ex);
        }
    }

    private void ConfigureFormats()
    {
        _logger.LogInformation("Configuring stateless decoder formats...");

        try
        {
            // Configure output format (H.264 input) for stateless decoder
            var outputFormat = new V4L2PixFormatMplane
            {
                Width = _configuration.InitialWidth,
                Height = _configuration.InitialHeight,
                PixelFormat = V4L2PixelFormats.V4L2_PIX_FMT_H264_SLICE,
                NumPlanes = 1,
                Field = (uint)V4L2Field.NONE,
                Colorspace = 5, // V4L2_COLORSPACE_REC709
                YcbcrEncoding = 1, // V4L2_YCBCR_ENC_DEFAULT
                Quantization = 1, // V4L2_QUANTIZATION_DEFAULT
                XferFunc = 1 // V4L2_XFER_FUNC_DEFAULT
            };
            _device.SetFormatMplane(V4L2BufferType.VIDEO_OUTPUT_MPLANE, outputFormat);

            // Configure capture format (decoded output)
            var captureFormat = new V4L2PixFormatMplane
            {
                Width = _configuration.InitialWidth,
                Height = _configuration.InitialHeight,
                PixelFormat = _configuration.PreferredPixelFormat,
                NumPlanes = 2, // NV12 typically has 2 planes
                Field = (uint)V4L2Field.NONE,
                Colorspace = 5,
                YcbcrEncoding = 1,
                Quantization = 1,
                XferFunc = 1
            };
            _device.SetFormatMplane(V4L2BufferType.VIDEO_CAPTURE_MPLANE, captureFormat);

            _logger.LogInformation("Format configuration completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure formats");
            throw new InvalidOperationException($"Format configuration failed: {ex.Message}", ex);
        }
    }

    private async Task SetupAndMapBuffersAsync()
    {
        _logger.LogInformation("Setting up and mapping buffers...");

        try
        {
            // Setup OUTPUT buffers for slice data
            await SetupBufferQueueAsync(V4L2BufferType.VIDEO_OUTPUT_MPLANE, _configuration.OutputBufferCount, _outputBuffers);
            
            // Setup CAPTURE buffers for decoded frames
            await SetupBufferQueueAsync(V4L2BufferType.VIDEO_CAPTURE_MPLANE, _configuration.CaptureBufferCount, _captureBuffers);

            _logger.LogInformation("Buffer setup completed: {OutputBuffers} output, {CaptureBuffers} capture", 
                _outputBuffers.Count, _captureBuffers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to setup buffers");
            throw new InvalidOperationException($"Buffer setup failed: {ex.Message}", ex);
        }

        await Task.CompletedTask;
    }

    private async Task SetupBufferQueueAsync(V4L2BufferType bufferType, uint bufferCount, List<MappedBuffer> bufferList)
    {
        // Request buffers from V4L2
        var reqBufs = new V4L2RequestBuffers
        {
            Count = bufferCount,
            Type = bufferType,
            Memory = V4L2Constants.V4L2_MEMORY_MMAP
        };

        var result = LibV4L2.RequestBuffers(_device.fd, ref reqBufs);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to request {bufferType} buffers: {result.ErrorMessage}");
        }

        _logger.LogDebug("Requested {RequestedCount} {BufferType} buffers, got {ActualCount}", 
            bufferCount, bufferType, reqBufs.Count);

        // For multiplanar buffers, we'll use a simplified approach
        // Create mapped buffers with allocated memory instead of V4L2 mmap
        for (uint i = 0; i < reqBufs.Count; i++)
        {
            // Allocate buffer memory (simplified approach for this demo)
            uint bufferSize = bufferType == V4L2BufferType.VIDEO_OUTPUT_MPLANE ? 
                _configuration.SliceBufferSize : 
                _configuration.InitialWidth * _configuration.InitialHeight * 2; // Assuming NV12 format

            unsafe
            {
                // Allocate unmanaged memory for the buffer
                var bufferPtr = Marshal.AllocHGlobal((int)bufferSize);
                
                // Create planes array
                var planes = new V4L2Plane[1];
                planes[0] = new V4L2Plane
                {
                    Length = bufferSize,
                    BytesUsed = 0
                };

                var mappedBuffer = new MappedBuffer
                {
                    Index = i,
                    Pointer = bufferPtr,
                    Size = bufferSize,
                    Planes = planes
                };

                bufferList.Add(mappedBuffer);
                
                _logger.LogTrace("Created buffer {Index} for {BufferType}: {Size} bytes at {Pointer:X8}", 
                    i, bufferType, bufferSize, bufferPtr.ToInt64());
            }
        }

        await Task.CompletedTask;
    }

    private async Task ExtractAndSetParameterSetsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Extracting parameter sets from video file...");

        try
        {
            var sps = await _parameterSetParser.ExtractSpsAsync(filePath);
            var pps = await _parameterSetParser.ExtractPpsAsync(filePath);

            if (sps.HasValue && pps.HasValue)
            {
                await _controlManager.SetParameterSetsAsync(_device.fd, sps.Value, pps.Value);
                _hasValidParameterSets = true;
                _logger.LogInformation("Parameter sets extracted and configured successfully");
            }
            else
            {
                throw new InvalidOperationException("Failed to extract valid SPS/PPS from video file");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract or set parameter sets");
            throw new InvalidOperationException($"Parameter set configuration failed: {ex.Message}", ex);
        }
    }

    private async Task ProcessVideoFileStatelessAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(filePath);
        long totalBytes = fileInfo.Length;
        
        // Process slice data
        await _sliceProcessor.ProcessVideoFileAsync(_device.fd, filePath,
            progress => ProgressChanged?.Invoke(this, new DecodingProgressEventArgs {
                BytesProcessed = (long)progress,
                TotalBytes = totalBytes,
                FramesDecoded = _framesDecoded,
                ElapsedTime = _decodingStopwatch.Elapsed
            }));

        // Update frame count
        _framesDecoded++;
    }

    private async Task ProcessVideoFileNaluByNaluStatelessAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(filePath);
        long totalBytes = fileInfo.Length;
        
        // Process NALUs one by one
        await _sliceProcessor.ProcessVideoFileNaluByNaluAsync(_device.fd, filePath,
            frame => {
                _framesDecoded++;
                FrameDecoded?.Invoke(this, new FrameDecodedEventArgs {
                    FrameNumber = _framesDecoded,
                    BufferIndex = 0,
                    BytesUsed = 0,
                    Timestamp = DateTime.Now
                });
            },
            progress => ProgressChanged?.Invoke(this, new DecodingProgressEventArgs {
                BytesProcessed = (long)progress,
                TotalBytes = totalBytes,
                FramesDecoded = _framesDecoded,
                ElapsedTime = _decodingStopwatch.Elapsed
            }));
    }

    private void StartStreaming()
    {
        _logger.LogInformation("Starting V4L2 streaming...");
        
        try
        {
            // Start streaming on both queues
            _device.StreamOn(V4L2BufferType.VIDEO_OUTPUT_MPLANE);
            _device.StreamOn(V4L2BufferType.VIDEO_CAPTURE_MPLANE);
            
            _logger.LogInformation("V4L2 streaming started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start streaming");
            throw new InvalidOperationException($"Failed to start streaming: {ex.Message}", ex);
        }
    }

    private void Cleanup()
    {
        if (!_isInitialized)
            return;

        _logger.LogInformation("Cleaning up decoder resources...");

        try
        {
            // Stop streaming
            if (_device?.fd > 0)
            {
                _device.StreamOff(V4L2BufferType.VIDEO_OUTPUT_MPLANE);
                _device.StreamOff(V4L2BufferType.VIDEO_CAPTURE_MPLANE);
            }

            // Unmap buffers
            UnmapBuffers(_outputBuffers);
            UnmapBuffers(_captureBuffers);

            _isInitialized = false;
            _logger.LogInformation("Decoder cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during cleanup");
        }
    }

    private void UnmapBuffers(List<MappedBuffer> buffers)
    {
        foreach (var buffer in buffers)
        {
            if (buffer.Pointer != IntPtr.Zero)
            {
                try
                {
                    // Free allocated memory instead of unmapping
                    Marshal.FreeHGlobal(buffer.Pointer);
                    _logger.LogTrace("Freed buffer {Index} memory at {Pointer:X8}", buffer.Index, buffer.Pointer.ToInt64());
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to free buffer {Index}", buffer.Index);
                }
            }
        }
        buffers.Clear();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Cleanup();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            Cleanup();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
        await Task.CompletedTask;
    }
}