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
    private uint _outputBufferCount = 4;
    private uint _captureBufferCount = 4;
    private int _framesDecoded;

    private bool _hasValidParameterSets;

    public event EventHandler<FrameDecodedEventArgs>? FrameDecoded;
    public event EventHandler<DecodingProgressEventArgs>? ProgressChanged;


    public H264V4L2StatelessDecoder(
        V4L2Device device,
        ILogger<H264V4L2StatelessDecoder> logger,
        DecoderConfiguration? configuration = null)
    {
        _device = device;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? new DecoderConfiguration();

        // Create parameter set parser
        var parserLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<H264ParameterSetParser>();
        _parameterSetParser = new H264ParameterSetParser(parserLogger, _configuration.InitialWidth, _configuration.InitialHeight);

        // Control manager will be created after device initialization
        _controlManager = null!; // Will be set in InitializeDecoderAsync
        _sliceProcessor = null!; // Will be set in InitializeDecoderAsync
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
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Initialize decoder and dependencies
            await InitializeDecoderAsync(cancellationToken);

            // Extract and set parameter sets
            await ExtractAndSetParameterSetsAsync(filePath, cancellationToken);

            // Process the file with stateless decoding
            await ProcessVideoFileStatelessAsync(filePath, cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation("Stateless decoding completed successfully. {FrameCount} frames in {ElapsedTime:F2}s ({FPS:F2} fps)",
                _framesDecoded, stopwatch.Elapsed.TotalSeconds, _framesDecoded / stopwatch.Elapsed.TotalSeconds);
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
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Initialize decoder and dependencies
            await InitializeDecoderAsync(cancellationToken);

            // Extract and set parameter sets
            await ExtractAndSetParameterSetsAsync(filePath, cancellationToken);

            // Process the file NALU by NALU with stateless decoding
            await ProcessVideoFileNaluByNaluStatelessAsync(filePath, cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation("Stateless NALU-by-NALU decoding completed successfully. {FrameCount} frames in {ElapsedTime:F2}s ({FPS:F2} fps)",
                _framesDecoded, stopwatch.Elapsed.TotalSeconds, _framesDecoded / stopwatch.Elapsed.TotalSeconds);
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
        // Create control manager and slice processor now that we have device FD
        var controlLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<V4L2StatelessControlManager>();
        var controlManager = new V4L2StatelessControlManager(controlLogger, _parameterSetParser, _device);

        var sliceLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<StatelessSliceProcessor>();
        var sliceProcessor = new StatelessSliceProcessor(sliceLogger, controlManager, _parameterSetParser, _device,
            _outputBuffers, _hasValidParameterSets);

        // Store references (hack around readonly requirement)
        object objControlManager = controlManager;
        object objSliceProcessor = sliceProcessor;
        var controlManagerField = typeof(H264V4L2StatelessDecoder).GetField("_controlManager",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var sliceProcessorField = typeof(H264V4L2StatelessDecoder).GetField("_sliceProcessor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        controlManagerField?.SetValue(this, objControlManager);
        sliceProcessorField?.SetValue(this, objSliceProcessor);

        // Configure decoder
        ConfigureFormats();
        await _controlManager.ConfigureStatelessControlsAsync(cancellationToken);
        SetupBuffers();
        StartStreaming();

        _logger.LogInformation("Decoder initialization completed successfully");
    }

    private void ConfigureFormats()
    {
        _logger.LogInformation("Configuring stateless decoder formats...");

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


        var captureFormat = new V4L2PixFormatMplane
        {
            Width = _configuration.InitialWidth,
            Height = _configuration.InitialHeight,
            PixelFormat = _configuration.PreferredPixelFormat,
            NumPlanes = 2, // NV12 typically has 2 planes
            Field = (uint)V4L2Field.NONE
        };
        _device.SetFormatMplane(V4L2BufferType.VIDEO_CAPTURE_MPLANE, captureFormat);

        //var result = LibV4L2.SetFormat(_deviceFd, ref captureFormat);
        //if (!result.Success)
        //{
        //    // Try alternative format
        //    capturePixMp.PixelFormat = _configuration.AlternativePixelFormat;
        //    capturePixMp.NumPlanes = 3; // YUV420 typically has 3 planes
        //    captureFormat.Pix_mp = capturePixMp;

        //    result = LibV4L2.SetFormat(_deviceFd, ref captureFormat);
        //    if (!result.Success)
        //    {
        //        throw new InvalidOperationException($"Failed to set capture format: {result.ErrorMessage}");
        //    }
        //}
    }

    private async Task ExtractAndSetParameterSetsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var sps = await _parameterSetParser.ExtractSpsAsync(filePath);
        var pps = await _parameterSetParser.ExtractPpsAsync(filePath);

        if (sps.HasValue && pps.HasValue)
        {
            await _controlManager.SetParameterSetsAsync(_device.fd, sps.Value, pps.Value);
            _hasValidParameterSets = true;
        }
        else
        {
            throw new InvalidOperationException("Failed to extract valid SPS/PPS from video file");
        }
    }

    private async Task ProcessVideoFileStatelessAsync(string filePath, CancellationToken cancellationToken = default)
    {
                // Process slice data
        await _sliceProcessor.ProcessVideoFileAsync(_device.fd, filePath,
            progress => ProgressChanged?.Invoke(this, new DecodingProgressEventArgs {
                BytesProcessed = (long)progress,
                TotalBytes = 100,
                FramesDecoded = _framesDecoded,
                ElapsedTime = TimeSpan.Zero
            }));

        // Update frame count
        _framesDecoded++;
    }

    private async Task ProcessVideoFileNaluByNaluStatelessAsync(string filePath, CancellationToken cancellationToken = default)
    {
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
                TotalBytes = 100,
                FramesDecoded = _framesDecoded,
                ElapsedTime = TimeSpan.Zero
            }));
    }

    private void SetupBuffers()
    {
        // Request OUTPUT buffers for slice data
        var outputReqBufs = new V4L2RequestBuffers
        {
            Count = (uint)_outputBufferCount,
            Type = V4L2BufferType.VIDEO_OUTPUT_MPLANE,
            Memory = V4L2Constants.V4L2_MEMORY_MMAP
        };

        var outputResult = LibV4L2.RequestBuffers(_device.fd, ref outputReqBufs);
        if (!outputResult.Success)
        {
            throw new InvalidOperationException($"Failed to request OUTPUT buffers: {outputResult.ErrorMessage}");
        }

        // Request CAPTURE buffers for decoded frames
        var captureReqBufs = new V4L2RequestBuffers
        {
            Count = (uint)_captureBufferCount,
            Type = V4L2BufferType.VIDEO_CAPTURE_MPLANE,
            Memory = V4L2Constants.V4L2_MEMORY_MMAP
        };

        var captureResult = LibV4L2.RequestBuffers(_device.fd, ref captureReqBufs);
        if (!captureResult.Success)
        {
            throw new InvalidOperationException($"Failed to request CAPTURE buffers: {captureResult.ErrorMessage}");
        }
    }

    private void StartStreaming()
    {
        // Start streaming on both queues
        _device.StreamOn(V4L2BufferType.VIDEO_OUTPUT_MPLANE);
        _device.StreamOn(V4L2BufferType.VIDEO_CAPTURE_MPLANE);
    }

    private void Cleanup()
    {

        _logger.LogInformation("Cleaning up decoder resources...");

        // Stop streaming
        _device.StreamOff(V4L2BufferType.VIDEO_OUTPUT_MPLANE);
        _device.StreamOff(V4L2BufferType.VIDEO_CAPTURE_MPLANE);

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

        _logger.LogInformation("Decoder cleanup completed");
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
    }
}