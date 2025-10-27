using System.Collections.Frozen;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.DmaBuffers;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;

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
    private readonly FrozenDictionary<PixelFormat, List<SharedDmaBuffer>> _managedDrmBuffers;
    private bool _disposed;

    public DrmBufferManager(
        DrmDevice drmDevice,
        DmaBuffersAllocator allocator,
        PixelFormat[] supportedPixelFormats,
        ILogger<DrmBufferManager> logger)
    {
        _drmDevice = drmDevice;
        _allocator = allocator;
        _logger = logger;
        _managedDrmBuffers =
            supportedPixelFormats.ToFrozenDictionary(format => format, format => new List<SharedDmaBuffer>());
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

        _managedDrmBuffers[pixelFormat].Add(managedBuffer);

        return managedBuffer;
    }


    public SharedDmaBuffer AllocateBuffer(
        uint width,
        uint height,
        PixelFormat pixelFormat)
    {
        var bufInfo = BuffersInfoProvider.GetBufferParams(width, height, pixelFormat);

        var buffer = _allocator.Allocate(bufInfo.FullSize);
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

        _managedDrmBuffers[pixelFormat].Add(managedBuffer);

        return managedBuffer;
    }

    /// <summary>
    /// Creates a DRM framebuffer for the given buffer.
    /// </summary>
    public unsafe uint CreateFramebuffer(SharedDmaBuffer buffer)
    {
        // Convert DMA-BUF FD to DRM handle
        var result = LibDrm.drmPrimeFDToHandle(_drmDevice.DeviceFd, buffer.DmaBuffer.Fd, out uint handle);
        if (result != 0)
        {
            return 0;
        }

        // Get buffer parameters from BuffersInfoProvider
        var bufferParams = BuffersInfoProvider.GetBufferParams(buffer.Width, buffer.Height, buffer.Format);

        uint* handles = stackalloc uint[4];
        uint* pitches = stackalloc uint[4];
        uint* offsets = stackalloc uint[4];

        // Configure handles, pitches and offsets based on plane count
        for (int i = 0; i < bufferParams.PlanesCount; i++)
        {
            handles[i] = handle; // Same handle for all planes in contiguous buffer
            pitches[i] = buffer.Stride > 0 ? buffer.Stride : bufferParams.Stride;
            offsets[i] = (uint)bufferParams.PlaneOffsets[i];
        }

        // Fill remaining slots with zeros
        for (int i = bufferParams.PlanesCount; i < 4; i++)
        {
            handles[i] = 0;
            pitches[i] = 0;
            offsets[i] = 0;
        }

        _logger.LogTrace(
            "[DRM] Creating {Format} framebuffer: Width={Width}, Height={Height}, Stride={Stride}, Planes={Planes}",
            buffer.Format.GetName(), buffer.Width, buffer.Height, pitches[0], bufferParams.PlanesCount);

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
            return 0;
        }

        return fbId;
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