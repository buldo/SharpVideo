using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SharpVideo.Linux.Native;
using SharpVideo.V4L2DecodeDemo.Interfaces;
using SharpVideo.H264;

namespace SharpVideo.V4L2DecodeDemo.Services.Stateless;

/// <summary>
/// Processes H.264 slice data for stateless decoders
/// </summary>
public class StatelessSliceProcessor : IStatelessSliceProcessor
{
    private readonly ILogger<StatelessSliceProcessor> _logger;
    private readonly IV4L2StatelessControlManager _controlManager;
    private readonly IH264ParameterSetParser _parameterSetParser;
    private readonly int _deviceFd;
    private readonly List<MappedBuffer> _outputBuffers;
    private readonly bool _hasValidParameterSets;

    public StatelessSliceProcessor(
        ILogger<StatelessSliceProcessor> logger,
        IV4L2StatelessControlManager controlManager,
        IH264ParameterSetParser parameterSetParser,
        int deviceFd,
        List<MappedBuffer> outputBuffers,
        bool hasValidParameterSets)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _controlManager = controlManager ?? throw new ArgumentNullException(nameof(controlManager));
        _parameterSetParser = parameterSetParser ?? throw new ArgumentNullException(nameof(parameterSetParser));
        _deviceFd = deviceFd;
        _outputBuffers = outputBuffers ?? throw new ArgumentNullException(nameof(outputBuffers));
        _hasValidParameterSets = hasValidParameterSets;
    }

    /// <inheritdoc />
    public byte[] ExtractSliceDataOnly(byte[] naluData, bool useStartCodes)
    {
        if (!useStartCodes)
        {
            // If decoder expects raw NALUs, remove start codes
            int naluStart = _parameterSetParser.GetNaluHeaderPosition(naluData, true);
            if (naluStart > 0 && naluStart < naluData.Length)
            {
                byte[] sliceOnly = new byte[naluData.Length - naluStart];
                Array.Copy(naluData, naluStart, sliceOnly, 0, sliceOnly.Length);
                return sliceOnly;
            }
        }

        // Return as-is if using start codes or no start code found
        return naluData;
    }

    /// <inheritdoc />
    public async Task QueueStatelessSliceDataAsync(byte[] sliceData, byte naluType, uint bufferIndex, CancellationToken cancellationToken)
    {
        if (!_hasValidParameterSets)
        {
            throw new InvalidOperationException("Parameter sets not available for stateless decoding");
        }

        // First, set the slice parameters controls
        await _controlManager.SetSliceParamsControlsAsync(sliceData, naluType, cancellationToken);

        // Extract only the slice data (without start codes if configured)
        // Note: useStartCodes should be passed from the main decoder
        byte[] pureSliceData = ExtractSliceDataOnly(sliceData, true); // TODO: Get from configuration

        _logger.LogDebug("Queuing stateless slice data: {SliceByteCount} bytes (NALU type {NaluType})",
            pureSliceData.Length, naluType);

        // Queue only the slice data to the hardware
        await QueueSliceDataToHardwareAsync(pureSliceData, bufferIndex, cancellationToken,
            isKeyFrame: naluType == 5); // IDR slice is keyframe
    }

    /// <inheritdoc />
    public async Task QueueStatelessFrameSlicesAsync(List<byte[]> frameSlices, uint bufferIndex, CancellationToken cancellationToken)
    {
        if (!_hasValidParameterSets || frameSlices.Count == 0)
        {
            return;
        }

        // Set slice parameters for the first slice (frame start)
        var firstSlice = frameSlices[0];
        int naluHeaderPos = _parameterSetParser.GetNaluHeaderPosition(firstSlice, true);
        if (naluHeaderPos < firstSlice.Length)
        {
            byte naluType = (byte)(firstSlice[naluHeaderPos] & 0x1F);
            await _controlManager.SetSliceParamsControlsAsync(firstSlice, naluType, cancellationToken);
        }

        // Combine all slice data (without parameter sets)
        var combinedSliceData = new List<byte>();
        bool isKeyFrame = false;

        foreach (var sliceData in frameSlices)
        {
            // Extract pure slice data
            byte[] pureSliceData = ExtractSliceDataOnly(sliceData, true); // TODO: Get from configuration
            combinedSliceData.AddRange(pureSliceData);

            // Check if this is an IDR slice (keyframe)
            int headerPos = _parameterSetParser.GetNaluHeaderPosition(sliceData, true);
            if (headerPos < sliceData.Length)
            {
                byte naluType = (byte)(sliceData[headerPos] & 0x1F);
                if (naluType == 5) // IDR slice
                    isKeyFrame = true;
            }
        }

        var frameData = combinedSliceData.ToArray();

        _logger.LogDebug("Queuing stateless frame slices: {SliceCount} slices = {TotalByteCount} bytes",
            frameSlices.Count, frameData.Length);

        await QueueSliceDataToHardwareAsync(frameData, bufferIndex, cancellationToken, isKeyFrame: isKeyFrame);
    }

    /// <inheritdoc />
    public bool IsFrameNalu(byte naluType)
    {
        return naluType switch
        {
            1 => true,  // Non-IDR slice
            5 => true,  // IDR slice
            2 => true,  // Data Partition A
            3 => true,  // Data Partition B
            4 => true,  // Data Partition C
            _ => false
        };
    }

    /// <inheritdoc />
    public bool IsFrameStartNalu(byte naluType)
    {
        return naluType switch
        {
            1 => true,  // Non-IDR slice (first slice of frame)
            5 => true,  // IDR slice (first slice of frame)
            9 => true,  // Access Unit Delimiter
            _ => false
        };
    }

    /// <summary>
    /// Queues slice data to the hardware decoder (for stateless operation)
    /// </summary>
    private async Task QueueSliceDataToHardwareAsync(byte[] sliceData, uint bufferIndex, CancellationToken cancellationToken, bool isKeyFrame = false, bool isEos = false)
    {
        await Task.Run(() =>
        {
            try
            {
                // Skip empty data
                if (sliceData.Length == 0)
                {
                    _logger.LogDebug("Skipping empty slice data buffer");
                    return;
                }

                var mappedBuffer = _outputBuffers[(int)bufferIndex];

                // Safety check for buffer size
                if (sliceData.Length > mappedBuffer.Size)
                {
                    _logger.LogWarning("Slice data ({DataSize} bytes) exceeds buffer size ({BufferSize} bytes), truncating",
                        sliceData.Length, mappedBuffer.Size);

                    // For stateless decoders, truncation may break decoding
                    if (isKeyFrame)
                    {
                        throw new InvalidOperationException($"Critical slice data too large for buffer: {sliceData.Length} > {mappedBuffer.Size}");
                    }

                    // Truncate if necessary
                    byte[] truncated = new byte[mappedBuffer.Size];
                    Array.Copy(sliceData, truncated, (int)mappedBuffer.Size);
                    sliceData = truncated;
                }

                // Copy slice data to mapped buffer
                Marshal.Copy(sliceData, 0, mappedBuffer.Pointer, sliceData.Length);

                // Setup buffer for queuing
                mappedBuffer.Planes[0].BytesUsed = (uint)sliceData.Length;

                // Set appropriate flags for stateless decoder
                uint flags = 0x01; // V4L2_BUF_FLAG_MAPPED

                if (isKeyFrame)
                {
                    flags |= 0x00000008; // V4L2_BUF_FLAG_KEYFRAME
                    _logger.LogDebug("Setting KEYFRAME flag for slice data");
                }

                // End-of-stream flag if needed
                if (isEos)
                {
                    flags |= 0x00100000; // V4L2_BUF_FLAG_LAST
                    _logger.LogDebug("Setting EOS flag");
                }

                // Create buffer structure for stateless decoding
                var buffer = new V4L2Buffer
                {
                    Index = bufferIndex,
                    Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE,
                    Memory = V4L2Constants.V4L2_MEMORY_MMAP,
                    Length = 1,
                    Field = (uint)V4L2Field.NONE,
                    BytesUsed = (uint)sliceData.Length,
                    Flags = flags,
                    Timestamp = new TimeVal { TvSec = 0, TvUsec = 0 },
                    Sequence = 0
                };

                unsafe
                {
                    buffer.Planes = (V4L2Plane*)Unsafe.AsPointer(ref mappedBuffer.Planes[0]);
                }

                // Queue the buffer containing only slice data
                var result = LibV4L2.QueueBuffer(_deviceFd, ref buffer);
                if (!result.Success)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    string errorDescription = GetErrorDescription(errorCode);
                    string v4l2Context = GetV4L2ErrorContext(errorCode) ?? string.Empty;

                    _logger.LogError("Failed to queue slice data buffer {Index}: {ErrorDesc} - {Context}",
                        bufferIndex, errorDescription, v4l2Context);

                    throw new InvalidOperationException(
                        $"Failed to queue slice data buffer {bufferIndex}: {errorDescription}. {v4l2Context}");
                }

                _logger.LogTrace("Queued {ByteCount} bytes of slice data to buffer {BufferIndex}", sliceData.Length, bufferIndex);
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                _logger.LogError(ex, "Unexpected error queuing slice data buffer {BufferIndex}", bufferIndex);
                throw new InvalidOperationException($"Failed to queue slice data buffer {bufferIndex}: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    // TODO: These utility methods should be moved to a shared utilities class
    private string GetErrorDescription(int errorCode)
    {
        return errorCode switch
        {
            22 => "EINVAL: Invalid argument",
            53 => "EBADR: Invalid request descriptor",
            74 => "EBADMSG: Bad message",
            5 => "EIO: Input/output error",
            11 => "EAGAIN: Resource temporarily unavailable",
            _ => $"Unknown error code: {errorCode}"
        };
    }

    private string GetV4L2ErrorContext(int errorCode)
    {
        return errorCode switch
        {
            22 => "Invalid argument - Check format configuration and buffer parameters",
            53 => "Invalid request descriptor - Data format doesn't match hardware expectations",
            74 => "Bad message - Input data is corrupted or incorrectly formatted",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Process slice data from video file
    /// </summary>
    public async Task ProcessVideoFileAsync(int deviceFd, string filePath, Action<double> progressCallback)
    {
        using var fileStream = File.OpenRead(filePath);
        using var naluProvider = new H264NaluProvider(NaluOutputMode.WithoutStartCode);

        // Read file data
        var buffer = new byte[fileStream.Length];
        await fileStream.ReadAsync(buffer, 0, buffer.Length);

        // Feed data to NALU provider
        await naluProvider.AppendData(buffer, CancellationToken.None);
        naluProvider.CompleteWriting();

        int processedNalus = 0;

        // Process slice NALUs
        await foreach (var naluData in naluProvider.NaluReader.ReadAllAsync(CancellationToken.None))
        {
            if (naluData.Length < 1) continue;

            byte naluType = (byte)(naluData[0] & 0x1F);
            if (naluType == 1 || naluType == 5) // Slice NALUs
            {
                await QueueSliceDataAsync(deviceFd, naluData);
                processedNalus++;

                // Simple progress reporting
                if (processedNalus % 10 == 0)
                {
                    progressCallback?.Invoke(processedNalus);
                }
            }
        }
    }

    /// <summary>
    /// Process video file NALU by NALU with frame callbacks
    /// </summary>
    public async Task ProcessVideoFileNaluByNaluAsync(int deviceFd, string filePath, Action<object> frameCallback, Action<double> progressCallback)
    {
        using var fileStream = File.OpenRead(filePath);
        using var naluProvider = new H264NaluProvider(NaluOutputMode.WithoutStartCode);

        // Read file data
        var buffer = new byte[fileStream.Length];
        await fileStream.ReadAsync(buffer, 0, buffer.Length);

        // Feed data to NALU provider
        await naluProvider.AppendData(buffer, CancellationToken.None);
        naluProvider.CompleteWriting();

        int processedNalus = 0;

        await foreach (var naluData in naluProvider.NaluReader.ReadAllAsync(CancellationToken.None))
        {
            if (naluData.Length < 1) continue;

            byte naluType = (byte)(naluData[0] & 0x1F);
            if (naluType == 1 || naluType == 5) // Slice NALUs
            {
                await QueueSliceDataAsync(deviceFd, naluData);

                // Try to dequeue a frame
                var frame = await DequeueFrameAsync(deviceFd);
                if (frame != null)
                {
                    frameCallback?.Invoke(frame);
                }

                processedNalus++;
                if (processedNalus % 10 == 0)
                {
                    progressCallback?.Invoke(processedNalus);
                }
            }
        }
    }

    /// <summary>
    /// Queue slice data to OUTPUT buffer
    /// </summary>
    public async Task QueueSliceDataAsync(int deviceFd, ReadOnlyMemory<byte> sliceData)
    {
        // Use existing method with some buffer index (simplified)
        await QueueStatelessSliceDataAsync(sliceData.ToArray(), (byte)(sliceData.Span[0] & 0x1F), 0, CancellationToken.None);
    }

    /// <summary>
    /// Dequeue and return decoded frame from CAPTURE buffer
    /// </summary>
    public async Task<object?> DequeueFrameAsync(int deviceFd)
    {
        try
        {
            var buffer = new V4L2Buffer
            {
                Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE,
                Memory = V4L2Constants.V4L2_MEMORY_MMAP
            };

            var result = LibV4L2.DequeueBuffer(deviceFd, ref buffer);
            if (result.Success)
            {
                // Frame data is available in the buffer
                // For now, return a placeholder object
                return new { BufferIndex = buffer.Index, Timestamp = buffer.Timestamp };
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dequeue frame");
            return null;
        }
        finally
        {
            await Task.CompletedTask;
        }
    }
}