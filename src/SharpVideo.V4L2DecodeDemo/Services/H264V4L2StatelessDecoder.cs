using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.H264;
using SharpVideo.Linux.Native;
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
public class H264V4L2StatelessDecoder : IVideoDecoder
{
    #region Dependencies and Configuration

    private readonly ILogger<H264V4L2StatelessDecoder> _logger;
    private readonly IV4L2DeviceManager _deviceManager;
    private readonly DecoderConfiguration _configuration;
    private readonly IH264ParameterSetParser _parameterSetParser;
    private readonly IV4L2StatelessControlManager _controlManager;
    private readonly IStatelessSliceProcessor _sliceProcessor;

    #endregion

    #region State

    private int _deviceFd = -1;
    private readonly List<MappedBuffer> _outputBuffers = new();
    private readonly List<MappedBuffer> _captureBuffers = new();
    private bool _disposed;
    private uint _outputBufferCount = 4;
    private uint _captureBufferCount = 4;
    private int _framesDecoded;

    // Stateless decoder specific state
    private V4L2CtrlH264Sps? _currentSps;
    private V4L2CtrlH264Pps? _currentPps;
    private bool _hasValidParameterSets;
    private bool _useStartCodes = true;

    #endregion

    #region Events

    public event EventHandler<FrameDecodedEventArgs>? FrameDecoded;
    public event EventHandler<DecodingProgressEventArgs>? ProgressChanged;

    #endregion

    #region Constructor

    public H264V4L2StatelessDecoder(
        ILogger<H264V4L2StatelessDecoder> logger,
        IV4L2DeviceManager? deviceManager = null,
        DecoderConfiguration? configuration = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? new DecoderConfiguration();

        var deviceLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<V4L2DeviceManager>();
        _deviceManager = deviceManager ?? new V4L2DeviceManager(deviceLogger);

        // Create parameter set parser
        var parserLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<H264ParameterSetParser>();
        _parameterSetParser = new H264ParameterSetParser(parserLogger, _configuration.InitialWidth, _configuration.InitialHeight);

        // Control manager will be created after device initialization
        _controlManager = null!; // Will be set in InitializeDecoderAsync
        _sliceProcessor = null!; // Will be set in InitializeDecoderAsync
    }

    #endregion

    #region Public API

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
            await CleanupAsync();
        }
    }

    #endregion

    #region Initialization

    private async Task InitializeDecoderAsync(CancellationToken cancellationToken)
    {
        // Find and open device
        var devicePath = _deviceManager.FindH264DecoderDevice();
        if (string.IsNullOrEmpty(devicePath))
        {
            throw new InvalidOperationException("No suitable H.264 decoder device found");
        }

        _deviceFd = Libc.open(devicePath, OpenFlags.O_RDWR | OpenFlags.O_NONBLOCK);
        if (_deviceFd < 0)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to open device {devicePath}. Error: {error}");
        }

        _logger.LogInformation("Opened decoder device: {DevicePath} (fd: {FileDescriptor})", devicePath, _deviceFd);

        try
        {
            // Create control manager and slice processor now that we have device FD
            var controlLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<V4L2StatelessControlManager>();
            var controlManager = new V4L2StatelessControlManager(controlLogger, _parameterSetParser, _deviceFd);

            var sliceLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<StatelessSliceProcessor>();
            var sliceProcessor = new StatelessSliceProcessor(sliceLogger, controlManager, _parameterSetParser, _deviceFd, _outputBuffers, _hasValidParameterSets);

            // Store references (hack around readonly requirement)
            object objControlManager = controlManager;
            object objSliceProcessor = sliceProcessor;
            var controlManagerField = typeof(H264V4L2StatelessDecoder).GetField("_controlManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var sliceProcessorField = typeof(H264V4L2StatelessDecoder).GetField("_sliceProcessor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            controlManagerField?.SetValue(this, objControlManager);
            sliceProcessorField?.SetValue(this, objSliceProcessor);

            // Configure decoder
            await ConfigureFormatsAsync(cancellationToken);
            _useStartCodes = await _controlManager.ConfigureStatelessControlsAsync(cancellationToken);
            await SetupBuffersAsync(cancellationToken);
            await StartStreamingAsync(cancellationToken);

            _logger.LogInformation("Decoder initialization completed successfully");
        }
        catch
        {
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
        _logger.LogInformation("Configuring stateless decoder formats...");

        // Configure output format (H.264 input) for stateless decoder
        var outputFormat = new V4L2Format
        {
            Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE
        };

        // Try common H.264 formats for stateless decoders, prioritizing S264 for rkvdec
        uint[] h264Formats = new uint[]
        {
            0x34363253, // S264 (H264 Parsed Slice Data) - preferred for rkvdec stateless
            0x34363248, // H264 (standard)
            0x31435641, // AVC1
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
                Colorspace = 5, // V4L2_COLORSPACE_REC709
                YcbcrEncoding = 1, // V4L2_YCBCR_ENC_DEFAULT
                Quantization = 1, // V4L2_QUANTIZATION_DEFAULT
                XferFunc = 1 // V4L2_XFER_FUNC_DEFAULT
            };
            outputFormat.Pix_mp = outputPixMp;

            var formatResult = LibV4L2.SetFormat(_deviceFd, ref outputFormat);
            if (formatResult.Success)
            {
                _logger.LogInformation("Successfully set H.264 format for stateless decoder: 0x{Format:X8}", format);
                formatSet = true;
                break;
            }
        }

        if (!formatSet)
        {
            throw new InvalidOperationException("Failed to set any supported H.264 format for stateless decoder");
        }

        // Configure capture format (decoded output)
        await ConfigureCaptureFormatAsync(cancellationToken);
    }

    private async Task ConfigureCaptureFormatAsync(CancellationToken cancellationToken)
    {
        var captureFormat = new V4L2Format
        {
            Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE
        };

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

        _logger.LogInformation("Set capture format: {Width}x{Height}, PixelFormat: 0x{PixelFormat:X8}",
            captureFormat.Pix_mp.Width, captureFormat.Pix_mp.Height, captureFormat.Pix_mp.PixelFormat);

        await Task.CompletedTask;
    }

    #endregion

    // TODO: Complete the implementation with the remaining methods
    // This is a foundational structure - the remaining methods would be simplified versions
    // of the buffer setup, streaming, parameter extraction, and processing methods

    #region Private Implementation Methods

    private async Task ExtractAndSetParameterSetsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var sps = await _parameterSetParser.ExtractSpsAsync(filePath);
        var pps = await _parameterSetParser.ExtractPpsAsync(filePath);

        if (sps.HasValue && pps.HasValue)
        {
            await _controlManager.SetParameterSetsAsync(_deviceFd, sps.Value, pps.Value);
            _currentSps = sps.Value;
            _currentPps = pps.Value;
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
        await _sliceProcessor.ProcessVideoFileAsync(_deviceFd, filePath,
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
        await _sliceProcessor.ProcessVideoFileNaluByNaluAsync(_deviceFd, filePath,
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

    private async Task SetupBuffersAsync(CancellationToken cancellationToken = default)
    {
        // Request OUTPUT buffers for slice data
        var outputReqBufs = new V4L2RequestBuffers
        {
            Count = (uint)_outputBufferCount,
            Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE,
            Memory = V4L2Constants.V4L2_MEMORY_MMAP
        };

        var outputResult = LibV4L2.RequestBuffers(_deviceFd, ref outputReqBufs);
        if (!outputResult.Success)
        {
            throw new InvalidOperationException($"Failed to request OUTPUT buffers: {outputResult.ErrorMessage}");
        }

        // Request CAPTURE buffers for decoded frames
        var captureReqBufs = new V4L2RequestBuffers
        {
            Count = (uint)_captureBufferCount,
            Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE,
            Memory = V4L2Constants.V4L2_MEMORY_MMAP
        };

        var captureResult = LibV4L2.RequestBuffers(_deviceFd, ref captureReqBufs);
        if (!captureResult.Success)
        {
            throw new InvalidOperationException($"Failed to request CAPTURE buffers: {captureResult.ErrorMessage}");
        }

        await Task.CompletedTask;
    }

    private async Task StartStreamingAsync(CancellationToken cancellationToken = default)
    {
        // Start streaming on both queues
        var outputResult = LibV4L2.StreamOn(_deviceFd, V4L2BufferType.VIDEO_OUTPUT_MPLANE);
        if (!outputResult.Success)
        {
            throw new InvalidOperationException($"Failed to start OUTPUT streaming: {outputResult.ErrorMessage}");
        }

        var captureResult = LibV4L2.StreamOn(_deviceFd, V4L2BufferType.VIDEO_CAPTURE_MPLANE);
        if (!captureResult.Success)
        {
            throw new InvalidOperationException($"Failed to start CAPTURE streaming: {captureResult.ErrorMessage}");
        }

        await Task.CompletedTask;
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

    #endregion
}