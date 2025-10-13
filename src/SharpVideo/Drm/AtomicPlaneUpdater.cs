using System.Runtime.Versioning;
using SharpVideo.Linux.Native;

namespace SharpVideo.Drm;

/// <summary>
/// Helper class for performing atomic plane updates with DRM.
/// Uses atomic modesetting API for better performance and atomicity.
/// </summary>
[SupportedOSPlatform("linux")]
public unsafe class AtomicPlaneUpdater : IDisposable
{
    private readonly int _drmFd;
    private readonly Dictionary<string, uint> _planePropertyIds = new();
    private readonly Dictionary<string, uint> _crtcPropertyIds = new();
    private bool _disposed;

    public AtomicPlaneUpdater(int drmFd, uint planeId, uint crtcId)
    {
        _drmFd = drmFd;

        // Get plane properties
        LoadObjectProperties(planeId, LibDrm.DRM_MODE_OBJECT_PLANE, _planePropertyIds);

        // Get CRTC properties (might be needed for some operations)
        LoadObjectProperties(crtcId, LibDrm.DRM_MODE_OBJECT_CRTC, _crtcPropertyIds);
    }

    private void LoadObjectProperties(uint objectId, uint objectType, Dictionary<string, uint> target)
    {
        var props = LibDrm.drmModeObjectGetProperties(_drmFd, objectId, objectType);
        if (props == null)
            return;

        try
        {
            for (int i = 0; i < props->CountProps; i++)
            {
                var propId = props->Props[i];
                var prop = LibDrm.drmModeGetProperty(_drmFd, propId);
                if (prop != null)
                {
                    try
                    {
                        var name = prop->NameString;
                        target[name] = propId;
                    }
                    finally
                    {
                        LibDrm.drmModeFreeProperty(prop);
                    }
                }
            }
        }
        finally
        {
            LibDrm.drmModeFreeObjectProperties(props);
        }
    }

    /// <summary>
    /// Update plane using atomic API for better performance.
    /// </summary>
    public bool UpdatePlane(
        uint planeId,
        uint crtcId,
        uint fbId,
        int crtcX, int crtcY,
        uint crtcW, uint crtcH,
        uint srcX, uint srcY,
        uint srcW, uint srcH,
        bool async = false)
    {
        var req = LibDrm.drmModeAtomicAlloc();
        if (req == null)
            return false;

        try
        {
            // Required properties for plane update
            if (!AddProperty(req, planeId, "FB_ID", fbId))
                return false;

            if (!AddProperty(req, planeId, "CRTC_ID", crtcId))
                return false;

            // Position and size on CRTC
            if (!AddProperty(req, planeId, "CRTC_X", (ulong)crtcX))
                return false;
            if (!AddProperty(req, planeId, "CRTC_Y", (ulong)crtcY))
                return false;
            if (!AddProperty(req, planeId, "CRTC_W", crtcW))
                return false;
            if (!AddProperty(req, planeId, "CRTC_H", crtcH))
                return false;

            // Source rectangle in framebuffer (16.16 fixed point)
            if (!AddProperty(req, planeId, "SRC_X", srcX))
                return false;
            if (!AddProperty(req, planeId, "SRC_Y", srcY))
                return false;
            if (!AddProperty(req, planeId, "SRC_W", srcW))
                return false;
            if (!AddProperty(req, planeId, "SRC_H", srcH))
                return false;

            // Commit flags
            var flags = DrmModeAtomicFlags.DRM_MODE_ATOMIC_NONBLOCK;

            if (async)
            {
                // Try async flip if supported (will fail gracefully if not)
                flags |= DrmModeAtomicFlags.DRM_MODE_PAGE_FLIP_ASYNC;
            }

            var result = LibDrm.drmModeAtomicCommit(_drmFd, req, flags, 0);
            return result == 0;
        }
        finally
        {
            LibDrm.drmModeAtomicFree(req);
        }
    }

    private bool AddProperty(DrmModeAtomicReq* req, uint objectId, string propertyName, ulong value)
    {
        if (!_planePropertyIds.TryGetValue(propertyName, out var propId))
        {
            // Property not found - might not be fatal for all properties
            return true; // Continue anyway
        }

        return LibDrm.drmModeAtomicAddProperty(req, objectId, propId, value) == 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
