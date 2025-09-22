using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SharpVideo.Linux.Native;
using SharpVideo.H264;
using System.Runtime.Versioning;
using SharpVideo.V4L2;

namespace SharpVideo.V4L2DecodeDemo.Services.Stateless;

/// <summary>
/// Processes H.264 slice data for stateless decoders
/// </summary>
[SupportedOSPlatform("linux")]
public class StatelessSliceProcessor
{
    private readonly ILogger<StatelessSliceProcessor> _logger;
    private readonly V4L2StatelessControlManager _controlManager;
    private readonly H264ParameterSetParser _parameterSetParser;
    private readonly V4L2Device _device;
    private readonly List<MappedBuffer> _outputBuffers;
    private readonly Func<bool> _hasValidParameterSetsProvider;

    public StatelessSliceProcessor(
        ILogger<StatelessSliceProcessor> logger,
        V4L2StatelessControlManager controlManager,
        H264ParameterSetParser parameterSetParser,
        V4L2Device device,
        List<MappedBuffer> outputBuffers,
        Func<bool> hasValidParameterSetsProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _controlManager = controlManager ?? throw new ArgumentNullException(nameof(controlManager));
        _parameterSetParser = parameterSetParser ?? throw new ArgumentNullException(nameof(parameterSetParser));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _outputBuffers = outputBuffers ?? throw new ArgumentNullException(nameof(outputBuffers));
        _hasValidParameterSetsProvider = hasValidParameterSetsProvider ?? throw new ArgumentNullException(nameof(hasValidParameterSetsProvider));
    }

    /// <summary>
    /// Extract only slice data (remove start codes if needed)
    /// </summary>
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

    /// <summary>
    /// Queue stateless slice data with proper control setup
    /// </summary>
    public async Task QueueStatelessSliceDataAsync(byte[] sliceData, H264NaluType naluType, uint bufferIndex, CancellationToken cancellationToken)
    {
        if (!_hasValidParameterSetsProvider())
        {
            throw new InvalidOperationException("Parameter sets not available for stateless decoding");
        }

        if (_outputBuffers.Count == 0)
        {
            throw new InvalidOperationException("Output buffers not initialized");
        }

        if (bufferIndex >= _outputBuffers.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferIndex), $"Buffer index {bufferIndex} exceeds available buffers ({_outputBuffers.Count})");
        }

        // First, set the slice parameters controls
        await _controlManager.SetSliceParamsControlsAsync(sliceData, naluType);

        // Extract only the slice data (without start codes if configured)
        // TODO: Get useStartCodes from configuration
        byte[] pureSliceData = ExtractSliceDataOnly(sliceData, true);

        _logger.LogDebug("Queuing stateless slice data: {SliceByteCount} bytes (NALU type {NaluType}) to buffer {BufferIndex}",
            pureSliceData.Length, naluType, bufferIndex);

        // Queue only the slice data to the hardware
        await QueueSliceDataToHardwareAsync(pureSliceData, bufferIndex, cancellationToken,
            isKeyFrame: naluType == H264NaluType.CodedSliceIdr); // IDR slice is keyframe
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

                // Critical: Don't truncate slice data as it will break decoding
                if (sliceData.Length > mappedBuffer.Size)
                {
                    throw new InvalidOperationException(
                        $"Slice data ({sliceData.Length} bytes) exceeds buffer size ({mappedBuffer.Size} bytes). " +
                        "This indicates insufficient buffer allocation or corrupted data.");
                }

                // Copy slice data to mapped buffer
                Marshal.Copy(sliceData, 0, mappedBuffer.Pointer, sliceData.Length);

                // Setup buffer for queuing - handle multiplanar properly
                if (mappedBuffer.Planes.Length > 0)
                {
                    mappedBuffer.Planes[0].BytesUsed = (uint)sliceData.Length;
                    mappedBuffer.Planes[0].Length = mappedBuffer.Size;
                }

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
                    Type = V4L2BufferType.VIDEO_OUTPUT_MPLANE,
                    Memory = V4L2Constants.V4L2_MEMORY_MMAP,
                    Length = (uint)mappedBuffer.Planes.Length,
                    Field = (uint)V4L2Field.NONE,
                    BytesUsed = (uint)sliceData.Length,
                    Flags = flags,
                    Timestamp = new TimeVal { TvSec = 0, TvUsec = 0 },
                    Sequence = 0
                };

                unsafe
                {
                    // Properly set planes pointer for multiplanar buffer
                    fixed (V4L2Plane* planePtr = mappedBuffer.Planes)
                    {
                        buffer.Planes = planePtr;
                    }
                }

                // Queue the buffer containing only slice data
                var result = LibV4L2.QueueBuffer(_device.fd, ref buffer);
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

    private string GetErrorDescription(int errorCode)
    {
        return errorCode switch
        {
            22 => "EINVAL: Invalid argument",
            53 => "EBADR: Invalid request descriptor",
            74 => "EBADMSG: Bad message",
            5 => "EIO: Input/output error",
            11 => "EAGAIN: Resource temporarily unavailable",
            16 => "EBUSY: Device or resource busy",
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
            11 => "Resource temporarily unavailable - Try again later or check buffer availability",
            16 => "Device busy - Decoder may be processing previous frames",
            _ => string.Empty
        };
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
                Type = V4L2BufferType.VIDEO_CAPTURE_MPLANE,
                Memory = V4L2Constants.V4L2_MEMORY_MMAP,
                Length = 1 // For multiplanar
            };

            // Try non-blocking dequeue
            var result = LibV4L2.DequeueBuffer(deviceFd, ref buffer);
            if (result.Success)
            {
                // Frame data is available in the buffer
                var frameInfo = new
                {
                    BufferIndex = buffer.Index,
                    Timestamp = buffer.Timestamp,
                    BytesUsed = buffer.BytesUsed,
                    Flags = buffer.Flags,
                    Sequence = buffer.Sequence,
                    IsKeyFrame = (buffer.Flags & 0x00000008) != 0,
                    IsLastFrame = (buffer.Flags & 0x00100000) != 0
                };

                _logger.LogTrace("Dequeued frame: Index={Index}, BytesUsed={BytesUsed}, Sequence={Sequence}",
                    frameInfo.BufferIndex, frameInfo.BytesUsed, frameInfo.Sequence);

                // Re-queue the capture buffer for continuous operation
                await RequeueCaptureBufferAsync(deviceFd, buffer.Index);

                return frameInfo;
            }
            else
            {
                // No frame available (non-blocking operation)
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error during frame dequeue operation");
            return null;
        }
        finally
        {
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Re-queue capture buffer for continuous operation
    /// </summary>
    private async Task RequeueCaptureBufferAsync(int deviceFd, uint bufferIndex)
    {
        try
        {
            var buffer = new V4L2Buffer
            {
                Index = bufferIndex,
                Type = V4L2BufferType.VIDEO_CAPTURE_MPLANE,
                Memory = V4L2Constants.V4L2_MEMORY_MMAP,
                Length = 1
            };

            var result = LibV4L2.QueueBuffer(deviceFd, ref buffer);
            if (!result.Success)
            {
                _logger.LogWarning("Failed to re-queue capture buffer {Index}: {Error}", bufferIndex, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error re-queuing capture buffer {Index}", bufferIndex);
        }

        await Task.CompletedTask;
    }
}