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
        // Convert Y plane DMA-BUF FD to DRM handle
        var result = LibDrm.drmPrimeFDToHandle(_drmDevice.DeviceFd, buffer.DmaBuffer.Fd, out uint yHandle);
        if (result != 0)
        {
            return 0;
        }

        //buffer.DrmHandle = yHandle;

        // Create framebuffer for NV12 format
        uint yPitch = buffer.Stride > 0 ? buffer.Stride : (uint)buffer.Width;
        uint uvPitch = yPitch; // UV plane has same stride as Y
        uint yOffset = 0;

        // Contiguous buffer - UV plane starts after Y plane data
        uint uvHandle = yHandle; // Same handle
        // Use stride * height for correct offset (accounts for padding)
        uint uvOffset = yPitch * (uint)buffer.Height;
        _logger.LogInformation($"[DRM] Creating NV12 framebuffer: Width={buffer.Width}, Height={buffer.Height}, Stride={yPitch}");

        uint* handles = stackalloc uint[4] { yHandle, uvHandle, 0, 0 };
        uint* pitches = stackalloc uint[4] { yPitch, uvPitch, 0, 0 };
        uint* offsets = stackalloc uint[4] { yOffset, uvOffset, 0, 0 };

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