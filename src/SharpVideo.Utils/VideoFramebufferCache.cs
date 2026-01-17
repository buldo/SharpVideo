using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Drm;
using SharpVideo.Linux.Native;
using SharpVideo.Utils.Buffers;

namespace SharpVideo.Utils;

/// <summary>
/// Internal cache for video DMA buffer framebuffers.
/// Creates and caches DRM framebuffers for SharedDmaBuffer objects.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class VideoFramebufferCache : IDisposable
{
    private const int MaxEntries = 32; // Must be >= CaptureBufferCount to avoid eviction

    private readonly int _drmFd;
    private readonly ILogger? _logger;

    private readonly (SharedDmaBuffer Buffer, uint FbId, uint Handle)[] _entries =
        new (SharedDmaBuffer, uint, uint)[MaxEntries];
    private int _count;
    private int _oldestIndex;
    private bool _disposed;

    /// <summary>
    /// Creates a new video framebuffer cache.
    /// </summary>
    /// <param name="drmFd">The DRM device file descriptor.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public VideoFramebufferCache(int drmFd, ILogger? logger = null)
    {
        _drmFd = drmFd;
        _logger = logger;
    }

    /// <summary>
    /// Gets or creates a framebuffer for the given SharedDmaBuffer.
    /// If the buffer is already in the cache, returns the cached FB ID.
    /// Otherwise creates a new framebuffer, evicting the oldest entry if necessary.
    /// </summary>
    /// <param name="buffer">The SharedDmaBuffer to get/create framebuffer for.</param>
    /// <returns>The framebuffer ID, or 0 on failure.</returns>
    public unsafe uint GetOrCreate(SharedDmaBuffer buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (buffer == null)
            return 0;

        // Check if already cached (by reference equality)
        for (int i = 0; i < _count; i++)
        {
            if (ReferenceEquals(_entries[i].Buffer, buffer))
            {
                return _entries[i].FbId;
            }
        }

        // Also check by DMA buffer FD (in case same buffer passed as different instance)
        for (int i = 0; i < _count; i++)
        {
            if (_entries[i].Buffer?.DmaBuffer.Fd == buffer.DmaBuffer.Fd)
            {
                return _entries[i].FbId;
            }
        }

        // Create new framebuffer
        // Convert DMA-BUF FD to DRM handle
        var result = LibDrm.drmPrimeFDToHandle(_drmFd, buffer.DmaBuffer.Fd, out uint handle);
        if (result != 0)
        {
            var errno = Marshal.GetLastPInvokeError();
            _logger?.LogError("drmPrimeFDToHandle failed: result={Result}, errno={Errno}, fd={Fd}",
                result, errno, buffer.DmaBuffer.Fd);
            return 0;
        }

        _logger?.LogTrace("drmPrimeFDToHandle: fd={Fd} -> handle={Handle}", buffer.DmaBuffer.Fd, handle);

        uint* handles = stackalloc uint[4];
        uint* pitches = stackalloc uint[4];
        uint* offsets = stackalloc uint[4];

        // Calculate plane layouts based on format
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

        _logger?.LogTrace(
            "drmModeAddFB2: {Width}x{Height}, format=0x{Format:X8}, planes={Planes}, pitches=[{P0},{P1},{P2},{P3}], offsets=[{O0},{O1},{O2},{O3}]",
            buffer.Width, buffer.Height, buffer.Format.Fourcc, planesCount,
            pitches[0], pitches[1], pitches[2], pitches[3],
            offsets[0], offsets[1], offsets[2], offsets[3]);

        var fbResult = LibDrm.drmModeAddFB2(
            _drmFd,
            buffer.Width,
            buffer.Height,
            buffer.Format.Fourcc,
            handles,
            pitches,
            offsets,
            out var fbId,
            0);

        if (fbResult != 0)
        {
            var errno = Marshal.GetLastPInvokeError();
            _logger?.LogError("drmModeAddFB2 failed: result={Result}, errno={Errno}", fbResult, errno);
            return 0;
        }

        _logger?.LogDebug("Created framebuffer {FbId} for buffer {Width}x{Height} format 0x{Format:X8}",
            fbId, buffer.Width, buffer.Height, buffer.Format.Fourcc);

        // Add to cache, evicting oldest if full
        if (_count < MaxEntries)
        {
            _entries[_count] = (buffer, fbId, handle);
            _count++;
        }
        else
        {
            // Evict oldest entry
            var oldEntry = _entries[_oldestIndex];
            if (oldEntry.FbId != 0)
            {
                LibDrm.drmModeRmFB(_drmFd, oldEntry.FbId);
            }
            // Note: We don't close the GEM handle here as DRM manages it

            _entries[_oldestIndex] = (buffer, fbId, handle);
            _oldestIndex = (_oldestIndex + 1) % MaxEntries;
        }

        return fbId;
    }

    /// <summary>
    /// Calculates plane pitches and offsets for a given format.
    /// </summary>
    private static (int planesCount, uint[] pitches, uint[] offsets) CalculatePlaneLayout(
        uint width, uint height, PixelFormat format, uint stride)
    {
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

        // Calculate based on actual stride
        if (format == KnownPixelFormats.DRM_FORMAT_NV12)
        {
            // NV12: Y plane + interleaved UV plane
            pitches[0] = stride;
            pitches[1] = stride;
            offsets[0] = 0;
            offsets[1] = stride * height;
        }
        else if (format == KnownPixelFormats.DRM_FORMAT_YUV420)
        {
            // YUV420 (I420): Y plane + U plane + V plane
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

            for (int i = 1; i < planesCount; i++)
            {
                pitches[i] = bufferParams.Planes[i].Pitch;
                offsets[i] = bufferParams.Planes[i].Offset;
            }
        }

        return (planesCount, pitches, offsets);
    }

    /// <summary>
    /// Invalidates all cache entries for a specific buffer.
    /// </summary>
    /// <param name="buffer">The buffer to invalidate.</param>
    public void Invalidate(SharedDmaBuffer buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        for (int i = 0; i < _count; i++)
        {
            if (ReferenceEquals(_entries[i].Buffer, buffer) ||
                _entries[i].Buffer?.DmaBuffer.Fd == buffer?.DmaBuffer.Fd)
            {
                if (_entries[i].FbId != 0)
                {
                    LibDrm.drmModeRmFB(_drmFd, _entries[i].FbId);
                }

                // Remove entry by shifting
                for (int j = i; j < _count - 1; j++)
                {
                    _entries[j] = _entries[j + 1];
                }

                _entries[_count - 1] = default;
                _count--;

                if (_oldestIndex >= _count && _count > 0)
                {
                    _oldestIndex = 0;
                }

                return;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        for (int i = 0; i < _count; i++)
        {
            if (_entries[i].FbId != 0)
            {
                LibDrm.drmModeRmFB(_drmFd, _entries[i].FbId);
            }
            _entries[i] = default;
        }

        _count = 0;
    }
}
