using System.Runtime.Versioning;

using SharpVideo.Linux.Native;

namespace SharpVideo.Utils;

/// <summary>
/// Internal cache for OSD GBM buffer object framebuffers.
/// Maintains a fixed-size cache of (BO, FB ID) pairs with LRU eviction.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class OsdFramebufferCache : IDisposable
{
    private const int MaxEntries = 4;
    
    private readonly int _drmFd;
    private readonly uint _width;
    private readonly uint _height;
    private readonly uint _format;
    
    private readonly (nint Bo, uint FbId)[] _entries = new (nint, uint)[MaxEntries];
    private int _count;
    private int _oldestIndex;
    private bool _disposed;

    /// <summary>
    /// Creates a new OSD framebuffer cache.
    /// </summary>
    /// <param name="drmFd">The DRM device file descriptor.</param>
    /// <param name="width">Width for framebuffers.</param>
    /// <param name="height">Height for framebuffers.</param>
    /// <param name="format">Pixel format fourcc for framebuffers.</param>
    public OsdFramebufferCache(int drmFd, uint width, uint height, uint format)
    {
        _drmFd = drmFd;
        _width = width;
        _height = height;
        _format = format;
    }

    /// <summary>
    /// Gets or creates a framebuffer for the given GBM buffer object.
    /// If the BO is already in the cache, returns the cached FB ID.
    /// Otherwise creates a new framebuffer, evicting the oldest entry if necessary.
    /// </summary>
    /// <param name="bo">The GBM buffer object handle.</param>
    /// <returns>The framebuffer ID, or 0 on failure.</returns>
    public unsafe uint GetOrCreate(nint bo)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (bo == 0)
            return 0;

        // Check if already cached
        for (int i = 0; i < _count; i++)
        {
            if (_entries[i].Bo == bo)
            {
                return _entries[i].FbId;
            }
        }

        // Create new framebuffer
        var handle = LibGbm.GetHandle(bo);
        var stride = LibGbm.GetStride(bo);

        uint* handles = stackalloc uint[4];
        uint* pitches = stackalloc uint[4];
        uint* offsets = stackalloc uint[4];

        handles[0] = handle;
        pitches[0] = stride;
        offsets[0] = 0;

        for (int i = 1; i < 4; i++)
        {
            handles[i] = 0;
            pitches[i] = 0;
            offsets[i] = 0;
        }

        var result = LibDrm.drmModeAddFB2(
            _drmFd,
            _width, _height,
            _format,
            handles, pitches, offsets,
            out var fbId, 0);

        if (result != 0)
        {
            return 0;
        }

        // Add to cache, evicting oldest if full
        if (_count < MaxEntries)
        {
            _entries[_count] = (bo, fbId);
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
            
            _entries[_oldestIndex] = (bo, fbId);
            _oldestIndex = (_oldestIndex + 1) % MaxEntries;
        }

        return fbId;
    }

    /// <summary>
    /// Gets the cached framebuffer ID for a BO without creating one.
    /// </summary>
    /// <param name="bo">The GBM buffer object handle.</param>
    /// <returns>The framebuffer ID if cached, 0 otherwise.</returns>
    public uint TryGet(nint bo)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        for (int i = 0; i < _count; i++)
        {
            if (_entries[i].Bo == bo)
            {
                return _entries[i].FbId;
            }
        }
        
        return 0;
    }

    /// <summary>
    /// Invalidates the cache entry for a specific BO.
    /// Does not destroy the framebuffer - call this when the BO is being released.
    /// </summary>
    /// <param name="bo">The GBM buffer object handle.</param>
    public void Invalidate(nint bo)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        for (int i = 0; i < _count; i++)
        {
            if (_entries[i].Bo == bo)
            {
                // Remove entry by shifting
                if (_entries[i].FbId != 0)
                {
                    LibDrm.drmModeRmFB(_drmFd, _entries[i].FbId);
                }
                
                for (int j = i; j < _count - 1; j++)
                {
                    _entries[j] = _entries[j + 1];
                }
                
                _entries[_count - 1] = default;
                _count--;
                
                // Adjust oldest index if needed
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
        
        // Destroy all cached framebuffers
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
