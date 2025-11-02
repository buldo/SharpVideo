using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;

namespace SharpVideo.Utils;

[SupportedOSPlatform("linux")]
public abstract class DrmSinglePlanePresenter
{
    private readonly uint _crtcId;
    private readonly uint _width;
    private readonly uint _height;
    private readonly DrmCapabilitiesState _capabilities;
    private readonly AtomicPlaneUpdater? _atomicUpdater;
    private readonly ILogger _logger;

    protected readonly DrmDevice _drmDevice;
    protected readonly DrmPlane _plane;

    protected DrmSinglePlanePresenter(
        DrmDevice drmDevice,
        DrmPlane plane,
        uint crtcId,
        uint width,
        uint height,
        DrmCapabilitiesState capabilities,
        AtomicPlaneUpdater? atomicUpdater,
        ILogger logger)
    {
        _drmDevice = drmDevice;
        _plane = plane;
        _crtcId = crtcId;
        _width = width;
        _height = height;
        _capabilities = capabilities;
        _atomicUpdater = atomicUpdater;
        _logger = logger;
    }

    public virtual void Cleanup()
    {
        try
        {
            if (_atomicUpdater != null)
            {
                _atomicUpdater.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during DrmSinglePlanePresenter cleanup");
        }
    }

    protected bool SetPlane(
        uint fbId,
        uint srcWidth,
        uint srcHeight)
    {
        if (_atomicUpdater != null)
        {
            var success = _atomicUpdater.UpdatePlane(
                _plane.Id,
                _crtcId,
                fbId,
                0, 0,
                _width, _height,
                0, 0,
                srcWidth << 16, srcHeight << 16,
                _capabilities.AsyncPageFlip);

            if (success)
            {
                return true;
            }

            _logger.LogWarning("Atomic plane update failed, falling back to legacy API");
        }

        var result = LibDrm.drmModeSetPlane(
            _drmDevice.DeviceFd,
            _plane.Id,
            _crtcId,
            fbId,
            0,
            0, 0, _width, _height,
            0, 0, srcWidth << 16, srcHeight << 16);
        if (result != 0)
        {
            _logger.LogError("Failed to set plane {PlaneId}: {Result}", _plane.Id, result);
            return false;
        }

        return true;
    }

}