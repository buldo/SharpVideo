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
    /// Allocates a pool of DMA buffers for NV12 format with separate planes.
    /// For V4L2 DMABUF compatibility, allocates separate buffers for Y and UV planes.
    /// </summary>
    public List<ManagedDrmBuffer> AllocateNv12Buffers(int width, int height, int count)
    {
        var buffers = new List<ManagedDrmBuffer>();

        for (int i = 0; i < count; i++)
        {
            // Allocate Y plane buffer
            ulong yPlaneSize = (ulong)(width * height);
            var yBuffer = _allocator.Allocate(yPlaneSize);
            if (yBuffer == null)
            {
                foreach (var buf in buffers) buf.Dispose();
                return new List<ManagedDrmBuffer>();
            }

            yBuffer.MapBuffer();
            if (yBuffer.MapStatus != MapStatus.Mapped)
            {
                yBuffer.Dispose();
                foreach (var buf in buffers) buf.Dispose();
                return new List<ManagedDrmBuffer>();
            }

            // Allocate UV plane buffer (half size of Y)
            ulong uvPlaneSize = (ulong)(width * height / 2);
            var uvBuffer = _allocator.Allocate(uvPlaneSize);
            if (uvBuffer == null)
            {
                yBuffer.UnmapBuffer();
                yBuffer.Dispose();
                foreach (var buf in buffers) buf.Dispose();
                return new List<ManagedDrmBuffer>();
            }

            uvBuffer.MapBuffer();
            if (uvBuffer.MapStatus != MapStatus.Mapped)
            {
                yBuffer.UnmapBuffer();
                yBuffer.Dispose();
                uvBuffer.Dispose();
                foreach (var buf in buffers) buf.Dispose();
                return new List<ManagedDrmBuffer>();
            }

            var managedBuffer = new ManagedDrmBuffer
            {
                DmaBuffer = yBuffer, // Y plane
                PlaneBuffers = new List<DmaBuffers.DmaBuffer> { uvBuffer }, // UV plane
                Width = width,
                Height = height,
                Format = KnownPixelFormats.DRM_FORMAT_NV12.Fourcc,
                Index = i
            };

            buffers.Add(managedBuffer);
            _buffers.Add(managedBuffer);
        }

        return buffers;
    }

    /// <summary>
    /// Allocates a pool of contiguous DMA buffers for NV12 format (single buffer with Y and UV planes).
    /// For V4L2 drivers that report NumPlanes=1 and expect a single DMA-BUF with plane offsets.
    /// </summary>
    public List<ManagedDrmBuffer> AllocateNv12ContiguousBuffers(int width, int height, int count)
    {
        var buffers = new List<ManagedDrmBuffer>();

        for (int i = 0; i < count; i++)
        {
            // Allocate single contiguous buffer for both Y and UV planes
            // Y plane: width * height
            // UV plane: width * height / 2
            ulong totalSize = (ulong)(width * height * 3.0 / 2.0);
            var buffer = _allocator.Allocate(totalSize);
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
                PlaneBuffers = null, // No separate plane buffers
                Width = width,
                Height = height,
                Format = KnownPixelFormats.DRM_FORMAT_NV12.Fourcc,
                Index = i
            };

            buffers.Add(managedBuffer);
            _buffers.Add(managedBuffer);
        }

        return buffers;
    }

    /// <summary>
    /// Allocates a pool of contiguous DMA buffers for NV12 format with explicit buffer size.
    /// For V4L2 drivers that report NumPlanes=1 with specific size requirements (including padding).
    /// </summary>
    public List<ManagedDrmBuffer> AllocateNv12ContiguousBuffersWithSize(int width, int height, int count, uint bufferSize)
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
                PlaneBuffers = null, // No separate plane buffers
                Width = width,
                Height = height,
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
        uint yPitch = (uint)buffer.Width;
        uint uvPitch = (uint)buffer.Width;
        uint yOffset = 0;
        uint uvOffset;
        uint uvHandle;

        if (buffer.PlaneBuffers != null && buffer.PlaneBuffers.Count > 0)
        {
            // Separate plane buffers
            result = LibDrm.drmPrimeFDToHandle(_drmDevice.DeviceFd, buffer.PlaneBuffers[0].Fd, out uvHandle);
            if (result != 0)
            {
                return 0;
            }
            uvOffset = 0; // Separate buffer, so offset is 0
        }
        else
        {
            // Contiguous buffer - UV plane is at offset width*height
            uvHandle = yHandle; // Same handle
            uvOffset = (uint)(buffer.Width * buffer.Height); // UV starts after Y plane
        }

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

    /// <summary>
    /// Removes a framebuffer.
    /// </summary>
    public void RemoveFramebuffer(uint fbId)
    {
        if (fbId != 0)
        {
            LibDrm.drmModeRmFB(_drmDevice.DeviceFd, fbId);
        }
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

/// <summary>
/// Represents a managed DRM buffer with associated metadata.
/// </summary>
[SupportedOSPlatform("linux")]
public class ManagedDrmBuffer : IDisposable
{
    public required DmaBuffers.DmaBuffer DmaBuffer { get; init; }

    /// <summary>
    /// Additional plane buffers for multi-planar formats (e.g., separate UV buffer for NV12)
    /// </summary>
    public List<DmaBuffers.DmaBuffer>? PlaneBuffers { get; init; }

    public int Width { get; init; }
    public int Height { get; init; }
    public uint Format { get; init; }
    public int Index { get; init; }
    public uint DrmHandle { get; set; }
    public uint FramebufferId { get; set; }

    public void Dispose()
    {
        DmaBuffer.UnmapBuffer();
        DmaBuffer.Dispose();

        if (PlaneBuffers != null)
        {
            foreach (var plane in PlaneBuffers)
            {
                plane.UnmapBuffer();
                plane.Dispose();
            }
        }
    }
}
