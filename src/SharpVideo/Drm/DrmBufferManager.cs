using System.Runtime.Versioning;
using SharpVideo.DmaBuffers;
using SharpVideo.Linux.Native;

namespace SharpVideo.Drm;

/// <summary>
/// Manages DMA buffers allocated for zero-copy sharing between V4L2 decoder and DRM display.
/// </summary>
[SupportedOSPlatform("linux")]
public class DrmBufferManager : IDisposable
{
    private readonly DrmDevice _drmDevice;
    private readonly DmaBuffersAllocator _allocator;
    private readonly List<ManagedDrmBuffer> _buffers = new();
    private bool _disposed;

    public DrmBufferManager(
        DrmDevice drmDevice,
        DmaBuffersAllocator allocator)
    {
        _drmDevice = drmDevice;
        _allocator = allocator;
    }

    /// <summary>
    /// Allocates a pool of contiguous DMA buffers for NV12 format with explicit buffer size.
    /// For V4L2 drivers that report NumPlanes=1 with specific size requirements (including padding).
    /// </summary>
    public List<ManagedDrmBuffer> AllocateNv12ContiguousBuffersWithSize(int width, int height, int count, uint bufferSize, uint stride)
    {
        var buffers = new List<ManagedDrmBuffer>();

        for (int i = 0; i < count; i++)
        {
            // Allocate single contiguous buffer using the exact size from V4L2
            var buffer = _allocator.Allocate(bufferSize);
            if (buffer == null)
            {
                foreach (var buf in buffers) buf.Dispose();
                return new List<ManagedDrmBuffer>();
            }

            buffer.MapBuffer();
            if (buffer.MapStatus != MapStatus.Mapped)
            {
                buffer.Dispose();
                foreach (var buf in buffers) buf.Dispose();
                return new List<ManagedDrmBuffer>();
            }

            var managedBuffer = new ManagedDrmBuffer
            {
                DmaBuffer = buffer, // Single buffer containing both Y and UV
                Width = width,
                Height = height,
                Stride = stride, // Actual stride from V4L2
                Format = KnownPixelFormats.DRM_FORMAT_NV12.Fourcc,
                Index = i
            };

            buffers.Add(managedBuffer);
            _buffers.Add(managedBuffer);
        }

        return buffers;
    }

    /// <summary>
    /// Creates a DRM framebuffer for the given buffer.
    /// </summary>
    public unsafe uint CreateFramebuffer(ManagedDrmBuffer buffer)
    {
        // Convert Y plane DMA-BUF FD to DRM handle
        var result = LibDrm.drmPrimeFDToHandle(_drmDevice.DeviceFd, buffer.DmaBuffer.Fd, out uint yHandle);
        if (result != 0)
        {
            return 0;
        }

        buffer.DrmHandle = yHandle;

        // Create framebuffer for NV12 format
        uint yPitch = buffer.Stride > 0 ? buffer.Stride : (uint)buffer.Width;
        uint uvPitch = yPitch; // UV plane has same stride as Y
        uint yOffset = 0;
        uint uvOffset;
        uint uvHandle;

        // Contiguous buffer - UV plane starts after Y plane data
        uvHandle = yHandle; // Same handle
        // Use stride * height for correct offset (accounts for padding)
        uvOffset = yPitch * (uint)buffer.Height;
        Console.Error.WriteLine($"[DRM] Creating NV12 framebuffer: Width={buffer.Width}, Height={buffer.Height}, Stride={yPitch}");
        Console.Error.WriteLine($"[DRM]   Y: handle={yHandle}, pitch={yPitch}, offset={yOffset}");
        Console.Error.WriteLine($"[DRM]   UV: handle={uvHandle}, pitch={uvPitch}, offset={uvOffset}");

        uint* handles = stackalloc uint[4] { yHandle, uvHandle, 0, 0 };
        uint* pitches = stackalloc uint[4] { yPitch, uvPitch, 0, 0 };
        uint* offsets = stackalloc uint[4] { yOffset, uvOffset, 0, 0 };

        var fbResult = LibDrm.drmModeAddFB2(
            _drmDevice.DeviceFd,
            (uint)buffer.Width,
            (uint)buffer.Height,
            buffer.Format,
            handles,
            pitches,
            offsets,
            out var fbId,
            0);

        if (fbResult != 0)
        {
            return 0;
        }

        buffer.FramebufferId = fbId;

        return fbId;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var buffer in _buffers)
        {
            if (buffer.FramebufferId != 0)
            {
                LibDrm.drmModeRmFB(_drmDevice.DeviceFd, buffer.FramebufferId);
            }
            buffer.Dispose();
        }

        _buffers.Clear();
        _disposed = true;
    }
}