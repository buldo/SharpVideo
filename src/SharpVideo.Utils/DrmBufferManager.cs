using System.Collections.Frozen;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.V4L2;
using SharpVideo.Utils.Buffers;

namespace SharpVideo.Utils;

/// <summary>
/// Manages DMA buffers allocated for zero-copy sharing between V4L2 decoder and DRM display.
/// </summary>
[SupportedOSPlatform("linux")]
public class DrmBufferManager : IDisposable
{
    private readonly DrmDevice _drmDevice;
    private readonly DmaBuffersAllocator _allocator;
    private readonly ILogger<DrmBufferManager> _logger;
    private readonly Dictionary<PixelFormat, List<SharedDmaBuffer>> _managedDrmBuffers = new();
    private bool _disposed;

    /// <summary>
    /// Gets the DRM device associated with this buffer manager.
    /// </summary>
    public DrmDevice DrmDevice => _drmDevice;

    public DrmBufferManager(
        DrmDevice drmDevice,
        DmaBuffersAllocator allocator,
        PixelFormat[] supportedPixelFormats,
        ILogger<DrmBufferManager> logger)
    {
        _drmDevice = drmDevice;
        _allocator = allocator;
        _logger = logger;
    }

    public List<SharedDmaBuffer> AllocateFromFormat(
        uint width,
        uint height,
        V4L2PlanePix planeFormat,
        uint buffersCount,
        PixelFormat pixelFormat)
    {
        var buffers = new List<SharedDmaBuffer>();

        for (int i = 0; i < buffersCount; i++)
        {
            var buffer = AllocateBuffer(width, height, planeFormat.SizeImage, planeFormat.BytesPerLine, pixelFormat);
            buffer.MapBuffer();
            buffers.Add(buffer);
        }

        return buffers;
    }

    private SharedDmaBuffer AllocateBuffer(
        uint width,
        uint height,
        uint fullSize,
        uint stride,
        PixelFormat pixelFormat)
    {
        var buffer = _allocator.Allocate(fullSize);
        if (buffer == null)
        {
            throw new Exception("Failed to allocate buffer");
        }

        var managedBuffer = new SharedDmaBuffer
        {
            DmaBuffer = buffer,
            Width = width,
            Height = height,
            Format = pixelFormat,
            Stride = stride
        };

        if(!_managedDrmBuffers.ContainsKey(pixelFormat))
        {
            _managedDrmBuffers[pixelFormat] = new List<SharedDmaBuffer>();
        }
        _managedDrmBuffers[pixelFormat].Add(managedBuffer);

        return managedBuffer;
    }


    public SharedDmaBuffer AllocateBuffer(
        uint width,
        uint height,
        PixelFormat pixelFormat)
    {
        var bufInfo = BuffersInfoProvider.GetBufferParams(width, height, pixelFormat);

        var buffer = _allocator.Allocate(bufInfo.TotalSize);
        if (buffer == null)
        {
            throw new Exception("Failed to allocate buffer");
        }

        var managedBuffer = new SharedDmaBuffer
        {
            DmaBuffer = buffer,
            Width = width,
            Height = height,
            Format = pixelFormat,
            Stride = bufInfo.Stride
        };

        if(!_managedDrmBuffers.ContainsKey(pixelFormat))
        {
            _managedDrmBuffers[pixelFormat] = new List<SharedDmaBuffer>();
        }
        _managedDrmBuffers[pixelFormat].Add(managedBuffer);

        return managedBuffer;
    }

    /// <summary>
    /// Creates a DRM framebuffer for the given buffer.
    /// </summary>
    public unsafe uint CreateFramebuffer(SharedDmaBuffer buffer)
    {
        // Check if framebuffer already exists - this should not be called if it does
        if (buffer.FramebufferId != 0)
        {
            _logger.LogWarning(
                "[DRM] CreateFramebuffer called but buffer already has FramebufferId={FbId}. " +
                "This indicates a logic error - framebuffer should be reused.",
                buffer.FramebufferId);
            return buffer.FramebufferId;
        }

        // Convert DMA-BUF FD to DRM handle
        var result = LibDrm.drmPrimeFDToHandle(_drmDevice.DeviceFd, buffer.DmaBuffer.Fd, out uint handle);
        if (result != 0)
        {
            return 0;
        }

        uint* handles = stackalloc uint[4];
        uint* pitches = stackalloc uint[4];
        uint* offsets = stackalloc uint[4];

        // Calculate plane layouts based on format
        // If buffer.Stride is provided (from V4L2), use it to calculate real offsets
        var (planesCount, calculatedPitches, calculatedOffsets) = CalculatePlaneLayout(
            buffer.Width, buffer.Height, buffer.Format, buffer.Stride);

        // Configure handles, pitches and offsets based on plane count
        for (int i = 0; i < planesCount; i++)
        {
            handles[i] = handle; // Same handle for all planes in contiguous buffer
            pitches[i] = calculatedPitches[i];
            offsets[i] = calculatedOffsets[i];
        }

        // Fill remaining slots with zeros
        for (int i = planesCount; i < 4; i++)
        {
            handles[i] = 0;
            pitches[i] = 0;
            offsets[i] = 0;
        }

        _logger.LogTrace(
            "[DRM] Creating {Format} framebuffer: Width={Width}, Height={Height}, Pitches=[{P0},{P1},{P2}], Offsets=[{O0},{O1},{O2}]",
            buffer.Format.GetName(), buffer.Width, buffer.Height,
            pitches[0], pitches[1], pitches[2],
            offsets[0], offsets[1], offsets[2]);

        var fbResult = LibDrm.drmModeAddFB2(
            _drmDevice.DeviceFd,
            (uint)buffer.Width,
            (uint)buffer.Height,
            buffer.Format.Fourcc,
            handles,
            pitches,
            offsets,
            out var fbId,
            0);

        if (fbResult != 0)
        {
            var errno = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            _logger.LogError(
                "[DRM] Failed to create framebuffer for {Format}: result={Result}, errno={Errno}",
                buffer.Format.GetName(), fbResult, errno);
            return 0;
        }

        _logger.LogDebug(
            "[DRM] Created framebuffer FbId={FbId} for buffer DmaFd={DmaFd}",
            fbId, buffer.DmaBuffer.Fd);

        return fbId;
    }

    /// <summary>
    /// Calculates plane pitches and offsets for a given format.
    /// If stride is provided (from V4L2), uses it to calculate correct offsets.
    /// </summary>
    private static (int planesCount, uint[] pitches, uint[] offsets) CalculatePlaneLayout(
        uint width, uint height, PixelFormat format, uint stride)
    {
        // Get base parameters from format database
        var bufferParams = BuffersInfoProvider.GetBufferParams(width, height, format);
        var planesCount = bufferParams.PlanesCount;

        var pitches = new uint[4];
        var offsets = new uint[4];

        // If no stride provided, use calculated values directly
        if (stride == 0)
        {
            for (int i = 0; i < planesCount; i++)
            {
                pitches[i] = bufferParams.Planes[i].Pitch;
                offsets[i] = bufferParams.Planes[i].Offset;
            }
            return (planesCount, pitches, offsets);
        }

        // Calculate based on actual stride from V4L2
        // This handles multi-plane formats where stride affects all plane offsets

        if (format == KnownPixelFormats.DRM_FORMAT_NV12)
        {
            // NV12: Y plane + interleaved UV plane
            // Y: stride * height
            // UV: stride * height / 2 (UV is half height, same stride as Y)
            pitches[0] = stride;
            pitches[1] = stride;
            offsets[0] = 0;
            offsets[1] = stride * height;
        }
        else if (format == KnownPixelFormats.DRM_FORMAT_YUV420)
        {
            // YUV420 (I420): Y plane + U plane + V plane
            // Y: stride * height
            // U: (stride/2) * (height/2)
            // V: (stride/2) * (height/2)
            uint ySize = stride * height;
            uint uvStride = stride / 2;
            uint uvSize = uvStride * (height / 2);

            pitches[0] = stride;
            pitches[1] = uvStride;
            pitches[2] = uvStride;
            offsets[0] = 0;
            offsets[1] = ySize;
            offsets[2] = ySize + uvSize;
        }
        else
        {
            // Single-plane formats or unknown multi-plane
            pitches[0] = stride;
            offsets[0] = 0;

            // For other planes, use calculated values
            for (int i = 1; i < planesCount; i++)
            {
                pitches[i] = bufferParams.Planes[i].Pitch;
                offsets[i] = bufferParams.Planes[i].Offset;
            }
        }

        return (planesCount, pitches, offsets);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var pair in _managedDrmBuffers)
        {
            foreach (var buffer in pair.Value)
            {
                if (buffer.FramebufferId != 0)
                {
                    LibDrm.drmModeRmFB(_drmDevice.DeviceFd, buffer.FramebufferId);
                }
                buffer.Dispose();
            }

            pair.Value.Clear();
        }

        _disposed = true;
    }
}