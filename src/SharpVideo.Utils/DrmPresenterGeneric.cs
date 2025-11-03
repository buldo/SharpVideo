using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Linux.Native;

namespace SharpVideo.Utils;

/// <summary>
/// DRM presenter with configurable primary and overlay plane presenters.
/// </summary>
/// <typeparam name="TPrimaryPresenter">Type of primary plane presenter</typeparam>
/// <typeparam name="TOverlayPresenter">Type of overlay plane presenter (use object if no overlay)</typeparam>
[SupportedOSPlatform("linux")]
public class DrmPresenter<TPrimaryPresenter, TOverlayPresenter>
    where TPrimaryPresenter : DrmSinglePlanePresenter
    where TOverlayPresenter : class
{
    private readonly ILogger _logger;

 private DrmPresenter(
        DrmPlane primaryPlane,
        TPrimaryPresenter primaryPlanePresenter,
        DrmPlane? overlayPlane,
     TOverlayPresenter? overlayPlanePresenter,
    ILogger logger)
    {
PrimaryPlane = primaryPlane;
      PrimaryPlanePresenter = primaryPlanePresenter;
  OverlayPlane = overlayPlane;
 OverlayPlanePresenter = overlayPlanePresenter;
  _logger = logger;
    }

  public DrmPlane PrimaryPlane { get; }

    public TPrimaryPresenter PrimaryPlanePresenter { get; }

    public DrmPlane? OverlayPlane { get; }

    public TOverlayPresenter? OverlayPlanePresenter { get; }

    /// <summary>
    /// Creates a DRM presenter with double-buffered DMA primary plane and optional overlay.
/// </summary>
public static DrmPresenter<DrmPlaneDoubleBufferPresenter, DrmPlaneLastDmaBufferPresenter>? CreateWithDmaBuffers(
  DrmDevice drmDevice,
        uint width,
        uint height,
        DrmBufferManager bufferManager,
     PixelFormat primaryPlanePixelFormat,
 PixelFormat? overlayPlanePixelFormat,
        ILogger logger)
    {
   var (primaryPlane, crtcId, connector, mode) = SetupPrimaryPlane(
 drmDevice, width, height, logger);

        DrmPlane? overlayPlane = null;
 DrmPlaneLastDmaBufferPresenter? overlayPlanePresenter = null;

      if (overlayPlanePixelFormat != null)
   {
       overlayPlane = FindOverlayPlane(drmDevice, crtcId, overlayPlanePixelFormat, logger);
     if (overlayPlane == null)
 {
            throw new Exception($"No overlay plane with {overlayPlanePixelFormat.GetName()} format found");
            }

 logger.LogInformation("Found {Format} overlay plane: ID {PlaneId}",
    overlayPlanePixelFormat.GetName(), overlayPlane.Id);

overlayPlanePresenter = new DrmPlaneLastDmaBufferPresenter(
       drmDevice,
  overlayPlane,
      crtcId,
  width,
        height,
   bufferManager,
         logger);
  }

  var primaryPlanePresenter = new DrmPlaneDoubleBufferPresenter(
drmDevice,
 primaryPlane,
    crtcId,
  width,
    height,
 logger,
bufferManager,
primaryPlanePixelFormat,
   connector.ConnectorId,
 mode);

     return new DrmPresenter<DrmPlaneDoubleBufferPresenter, DrmPlaneLastDmaBufferPresenter>(
      primaryPlane,
  primaryPlanePresenter,
overlayPlane,
    overlayPlanePresenter,
      logger);
    }

  /// <summary>
    /// Creates a DRM presenter with GBM-based primary plane for OpenGL ES rendering.
    /// </summary>
    public static DrmPresenter<DrmPlaneGbmPresenter, TOverlay>? CreateWithGbmBuffers<TOverlay>(
        DrmDevice drmDevice,
        uint width,
        uint height,
        GbmDevice gbmDevice,
        PixelFormat primaryPlanePixelFormat,
     ILogger logger)
        where TOverlay : class
    {
      var (primaryPlane, crtcId, connector, mode) = SetupPrimaryPlane(
     drmDevice, width, height, logger);

        var primaryPlanePresenter = new DrmPlaneGbmPresenter(
     drmDevice,
      primaryPlane,
  crtcId,
  width,
            height,
 logger,
            gbmDevice,
    primaryPlanePixelFormat,
  connector.ConnectorId,
     mode);

        return new DrmPresenter<DrmPlaneGbmPresenter, TOverlay>(
          primaryPlane,
      primaryPlanePresenter,
        null,
   null,
            logger);
    }

  private static (DrmPlane primaryPlane, uint crtcId, DrmConnector connector, DrmModeInfo mode) SetupPrimaryPlane(
        DrmDevice drmDevice,
        uint width,
   uint height,
   ILogger logger)
    {
      var resources = drmDevice.GetResources();
      if (resources == null)
  {
          throw new Exception("Failed to get DRM resources");
        }

     var connector = resources.Connectors.FirstOrDefault(c => c.Connection == DrmModeConnection.Connected);
     if (connector == null)
     {
  throw new Exception("No connected display found");
        }

        logger.LogInformation("Found connected display: {Type}", connector.ConnectorType);

   var mode = connector.Modes.FirstOrDefault(m => m.HDisplay == width && m.VDisplay == height);
        if (mode == null)
        {
  throw new Exception($"No {width}x{height} mode found");
        }

        logger.LogInformation("Using mode: {Name} ({Width}x{Height}@{RefreshRate}Hz)",
      mode.Name, mode.HDisplay, mode.VDisplay, mode.VRefresh);

    var encoder = connector.Encoder ?? connector.Encoders.FirstOrDefault();
     if (encoder == null)
   {
    throw new Exception("No encoder found");
        }

        var crtcId = encoder.CrtcId;
        if (crtcId == 0)
        {
       var availableCrtcs = resources.Crtcs
      .Where(crtc => (encoder.PossibleCrtcs & (1u << Array.IndexOf(resources.Crtcs.ToArray(), crtc))) != 0);
            crtcId = availableCrtcs.FirstOrDefault();
        }

 if (crtcId == 0)
    {
     throw new Exception("No available CRTC found");
  }

 logger.LogInformation("Using CRTC ID: {CrtcId}", crtcId);

var crtcIndex = resources.Crtcs.ToList().IndexOf(crtcId);
 var compatiblePlanes = resources.Planes
    .Where(p => (p.PossibleCrtcs & (1u << crtcIndex)) != 0)
   .ToList();

   var primaryPlane = compatiblePlanes.FirstOrDefault(p =>
  {
    var props = p.GetProperties();
       var typeProp = props.FirstOrDefault(prop => prop.Name.Equals("type", StringComparison.OrdinalIgnoreCase));
  return typeProp != null && typeProp.EnumNames != null &&
    typeProp.Value < (ulong)typeProp.EnumNames.Count &&
          typeProp.EnumNames[(int)typeProp.Value].Equals("Primary", StringComparison.OrdinalIgnoreCase);
        });

     if (primaryPlane == null)
        {
 throw new Exception("No primary plane found");
        }

      logger.LogInformation("Found primary plane: ID {PlaneId}", primaryPlane.Id);

#if DEBUG
        var capabilities = drmDevice.GetDeviceCapabilities();
   DumpCapabilities(capabilities, logger);
#endif

      return (primaryPlane, crtcId, connector, mode);
    }

    private static DrmPlane? FindOverlayPlane(
        DrmDevice drmDevice,
   uint crtcId,
    PixelFormat pixelFormat,
  ILogger logger)
    {
        var resources = drmDevice.GetResources();
     if (resources == null)
 {
      return null;
      }

  var crtcIndex = resources.Crtcs.ToList().IndexOf(crtcId);
   var compatiblePlanes = resources.Planes
  .Where(p => (p.PossibleCrtcs & (1u << crtcIndex)) != 0)
 .ToList();

  return compatiblePlanes.FirstOrDefault(p =>
   {
         var props = p.GetProperties();
        var typeProp = props.FirstOrDefault(prop => prop.Name.Equals("type", StringComparison.OrdinalIgnoreCase));
            bool isOverlay = typeProp != null && typeProp.EnumNames != null &&
    typeProp.Value < (ulong)typeProp.EnumNames.Count &&
     typeProp.EnumNames[(int)typeProp.Value]
    .Equals("Overlay", StringComparison.OrdinalIgnoreCase);
    return isOverlay && p.Formats.Contains(pixelFormat.Fourcc);
 });
    }

private static void DumpCapabilities(DrmCapabilitiesState caps, ILogger logger)
    {
        logger.LogInformation(
            "DRM device capabilities: " +
      "DumbBuffer: {DumbBuffer}; " +
   "VblankHighCrtc: {VblankHighCrtc}; " +
       "DumbPreferredDepth: {DumbPreferredDepth}; " +
     "DumbPreferShadow: {DumbPreferShadow}; " +
       "Prime: {Prime}; " +
       "TimestampMonotonic: {TimestampMonotonic}; " +
   "AsyncPageFlip: {AsyncPageFlip}; " +
      "CursorWidth: {CursorWidth}; " +
      "CursorHeight: {CursorHeight}; " +
"AddFB2Modifiers: {AddFB2Modifiers}; " +
   "PageFlipTarget: {PageFlipTarget}; " +
     "CrtcInVblankEvent: {CrtcInVblankEvent}; " +
         "SyncObj: {SyncObj}; " +
    "SyncObjTimeline: {SyncObjTimeline}; " +
       "AtomicAsyncPageFlip: {AtomicAsyncPageFlip}",
  caps.DumbBuffer,
 caps.VblankHighCrtc,
         caps.DumbPreferredDepth,
      caps.DumbPreferShadow,
  caps.Prime,
     caps.TimestampMonotonic,
       caps.AsyncPageFlip,
   caps.CursorWidth,
caps.CursorHeight,
            caps.AddFB2Modifiers,
            caps.PageFlipTarget,
caps.CrtcInVblankEvent,
    caps.SyncObj,
  caps.SyncObjTimeline,
      caps.AtomicAsyncPageFlip);
    }

    public void CleanupDisplay()
    {
        _logger.LogInformation("Cleaning up display resources");

        try
        {
        PrimaryPlanePresenter.Cleanup();
     if (OverlayPlanePresenter is DrmSinglePlanePresenter overlayPresenter)
        {
      overlayPresenter.Cleanup();
     }
 }
        catch (Exception ex)
     {
    _logger.LogError(ex, "Error during display cleanup");
      }
  }
}
