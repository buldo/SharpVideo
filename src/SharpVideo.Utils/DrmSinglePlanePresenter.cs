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
        ILogger logger)
    {
        _drmDevice = drmDevice;
        _plane = plane;
        _crtcId = crtcId;
        _width = width;
        _height = height;
        _capabilities = capabilities;
        _logger = logger;
    }

    public virtual void Cleanup()
    {
    }

    protected bool SetPlane(
        uint fbId,
        uint srcWidth,
        uint srcHeight)
    {
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