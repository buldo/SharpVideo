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
    private readonly ILogger _logger;

    protected readonly DrmDevice _drmDevice;
    protected readonly DrmPlane _plane;

    protected DrmSinglePlanePresenter(
        DrmDevice drmDevice,
        DrmPlane plane,
        uint crtcId,
        uint width,
        uint height,
        ILogger logger)
    {
        _drmDevice = drmDevice;
        _plane = plane;
        _crtcId = crtcId;
        _width = width;
        _height = height;
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

    /// <summary>
    /// Sets the CRTC mode using legacy API.
    /// Used for initial display setup.
    /// </summary>
    protected static unsafe bool SetCrtcMode(
        DrmDevice drmDevice,
        uint crtcId,
        uint connectorId,
        uint fbId,
        DrmModeInfo mode,
        uint width,
        uint height,
        ILogger logger)
    {
        var nativeMode = new DrmModeModeInfo
        {
            Clock = mode.Clock,
            HDisplay = mode.HDisplay,
            HSyncStart = mode.HSyncStart,
            HSyncEnd = mode.HSyncEnd,
            HTotal = mode.HTotal,
            HSkew = mode.HSkew,
            VDisplay = mode.VDisplay,
            VSyncStart = mode.VSyncStart,
            VSyncEnd = mode.VSyncEnd,
            VTotal = mode.VTotal,
            VScan = mode.VScan,
            VRefresh = mode.VRefresh,
            Flags = mode.Flags,
            Type = mode.Type
        };

        var nameBytes = System.Text.Encoding.UTF8.GetBytes(mode.Name);
        for (int i = 0; i < Math.Min(nameBytes.Length, 32); i++)
        {
            nativeMode.Name[i] = nameBytes[i];
        }

        var result = LibDrm.drmModeSetCrtc(drmDevice.DeviceFd, crtcId, fbId, 0, 0, &connectorId, 1, &nativeMode);
        if (result != 0)
        {
            var errno = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            logger.LogError("Failed to set CRTC mode: result={Result}, errno={Errno}", result, errno);
            return false;
        }

        logger.LogInformation("Successfully set CRTC to mode {Name} ({Width}x{Height}@{RefreshRate}Hz)",
            mode.Name, mode.HDisplay, mode.VDisplay, mode.VRefresh);
        return true;
    }
}