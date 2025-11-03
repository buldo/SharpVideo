using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.Gbm;

namespace SharpVideo.Utils;

/// <summary>
/// DRM plane presenter using GBM (Generic Buffer Manager) buffers for OpenGL ES rendering.
/// This presenter manages double-buffering with GBM buffer objects suitable for GPU rendering.
/// </summary>
[SupportedOSPlatform("linux")]
public class DrmPlaneGbmPresenter : DrmSinglePlanePresenter
{
    private readonly GbmDevice _gbmDevice;
    private readonly GbmSurface _gbmSurface;
    private readonly uint _width;
    private readonly uint _height;
    private readonly PixelFormat _format;
    private readonly ILogger _logger;

  private nint _currentBo;
    private nint _previousBo;
    private uint _currentFbId;
    private uint _previousFbId;

    public DrmPlaneGbmPresenter(
        DrmDevice drmDevice,
  DrmPlane plane,
        uint crtcId,
        uint width,
        uint height,
 ILogger logger,
    GbmDevice gbmDevice,
    PixelFormat format,
        uint connectorId,
        DrmModeInfo mode)
    : base(drmDevice, plane, crtcId, width, height, logger)
    {
   _gbmDevice = gbmDevice;
        _width = width;
  _height = height;
     _format = format;
     _logger = logger;

        // Create GBM surface for scanout and rendering
        _gbmSurface = _gbmDevice.CreateSurface(
            width,
            height,
        format,
            GbmBoUse.GBM_BO_USE_SCANOUT | GbmBoUse.GBM_BO_USE_RENDERING);

        _logger.LogInformation("Created GBM surface for primary plane: {Width}x{Height} {Format}",
         width, height, format.GetName());

      // Lock front buffer to get initial BO
        _currentBo = LibGbm.LockFrontBuffer(_gbmSurface.Fd);
        if (_currentBo == 0)
        {
            _gbmSurface.Dispose();
 throw new Exception("Failed to lock front buffer from GBM surface");
        }

    // Create framebuffer for initial BO
        _currentFbId = CreateFramebufferForBo(_currentBo);
        if (_currentFbId == 0)
        {
          LibGbm.ReleaseBuffer(_gbmSurface.Fd, _currentBo);
            _gbmSurface.Dispose();
     throw new Exception("Failed to create framebuffer for initial BO");
        }

   // Set CRTC mode with primary plane
        if (!SetCrtcMode(drmDevice, crtcId, connectorId, _currentFbId, mode, width, height, logger))
  {
            Cleanup();
throw new Exception("Failed to set CRTC mode");
        }

  // Set primary plane with initial buffer
 if (!SetPlane(_currentFbId, width, height))
        {
        Cleanup();
          throw new Exception("Failed to set plane");
        }

        _logger.LogInformation("GBM primary plane presenter initialized successfully");
    }

    /// <summary>
    /// Gets the GBM surface for OpenGL ES rendering.
    /// Render to this surface, then call SwapBuffers() to present.
    /// </summary>
    public GbmSurface GetGbmSurface()
    {
        return _gbmSurface;
    }

    /// <summary>
    /// Gets the native GBM surface handle for EGL.
    /// </summary>
    public nint GetNativeGbmSurfaceHandle()
    {
        return _gbmSurface.Fd;
    }

    /// <summary>
    /// Swaps buffers after OpenGL ES rendering and presents the new frame.
    /// This locks the front buffer from GBM, creates a framebuffer, and updates the plane.
    /// </summary>
    public bool SwapBuffers()
    {
        // Lock the new front buffer (the one we just rendered to)
        var newBo = LibGbm.LockFrontBuffer(_gbmSurface.Fd);
    if (newBo == 0)
     {
          _logger.LogError("Failed to lock front buffer from GBM surface");
        return false;
        }

        // Create framebuffer for the new BO
        var newFbId = CreateFramebufferForBo(newBo);
        if (newFbId == 0)
        {
  _logger.LogError("Failed to create framebuffer for new BO");
      LibGbm.ReleaseBuffer(_gbmSurface.Fd, newBo);
return false;
        }

        // Update the plane to show the new buffer
        var success = SetPlane(newFbId, _width, _height);
        if (!success)
        {
        _logger.LogError("Failed to update plane with new buffer");
       LibDrm.drmModeRmFB(_drmDevice.DeviceFd, newFbId);
            LibGbm.ReleaseBuffer(_gbmSurface.Fd, newBo);
        return false;
        }

        // Clean up previous buffer (if any)
        if (_previousBo != 0)
        {
            LibGbm.ReleaseBuffer(_gbmSurface.Fd, _previousBo);
        }
    if (_previousFbId != 0)
        {
            LibDrm.drmModeRmFB(_drmDevice.DeviceFd, _previousFbId);
    }

        // Update state
      _previousBo = _currentBo;
        _previousFbId = _currentFbId;
        _currentBo = newBo;
   _currentFbId = newFbId;

        return true;
    }

    private uint CreateFramebufferForBo(nint bo)
    {
        // Get buffer object properties
        var width = LibGbm.GetWidth(bo);
        var height = LibGbm.GetHeight(bo);
   var stride = LibGbm.GetStride(bo);
  var handle = LibGbm.GetHandle(bo);

        if (handle == 0)
      {
     _logger.LogError("Failed to get BO handle");
        return 0;
        }

    _logger.LogDebug("Creating framebuffer for BO: width={Width}, height={Height}, stride={Stride}, handle={Handle}",
        width, height, stride, handle);

      // Create framebuffer
    unsafe
        {
    uint* handles = stackalloc uint[4];
            uint* pitches = stackalloc uint[4];
     uint* offsets = stackalloc uint[4];

            handles[0] = handle;
            pitches[0] = stride;
     offsets[0] = 0;

        // Fill remaining slots with zeros
    for (int i = 1; i < 4; i++)
            {
    handles[i] = 0;
     pitches[i] = 0;
       offsets[i] = 0;
      }

     var result = LibDrm.drmModeAddFB2(
            _drmDevice.DeviceFd,
   width,
     height,
   _format.Fourcc,
          handles,
           pitches,
    offsets,
 out var fbId,
  0);

         if (result != 0)
   {
       _logger.LogError("drmModeAddFB2 failed with error {Error}", result);
     return 0;
          }

            _logger.LogTrace("Created framebuffer ID {FbId} for BO", fbId);
 return fbId;
        }
    }

    public override void Cleanup()
 {
    base.Cleanup();

        _logger.LogInformation("Cleaning up GBM presenter resources");

        // Clean up current buffer
    if (_currentFbId != 0)
        {
            LibDrm.drmModeRmFB(_drmDevice.DeviceFd, _currentFbId);
   _currentFbId = 0;
        }
        if (_currentBo != 0)
        {
        LibGbm.ReleaseBuffer(_gbmSurface.Fd, _currentBo);
    _currentBo = 0;
     }

    // Clean up previous buffer
      if (_previousFbId != 0)
        {
       LibDrm.drmModeRmFB(_drmDevice.DeviceFd, _previousFbId);
      _previousFbId = 0;
        }
        if (_previousBo != 0)
        {
            LibGbm.ReleaseBuffer(_gbmSurface.Fd, _previousBo);
            _previousBo = 0;
        }

     // Dispose GBM surface (this will be handled by GbmDevice)
        _logger.LogInformation("GBM presenter cleanup complete");
    }

    private static unsafe bool SetCrtcMode(
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
            logger.LogError("Failed to set CRTC mode: {Result}", result);
    return false;
        }

 logger.LogInformation("Successfully set CRTC to mode {Name}", mode.Name);
        return true;
    }
}
