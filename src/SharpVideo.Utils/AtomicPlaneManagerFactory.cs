using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Drm;

namespace SharpVideo.Utils;

[SupportedOSPlatform("linux")]
public static class AtomicPlaneManagerFactory
{
    public static bool TryToCreate(
        DrmDevice drmDevice,
        DrmPlane plane,
        uint crtcId,
        uint srcWidth,
        uint srcHeight,
        uint dstWidth,
        uint dstHeight,
        ILogger logger,
        out AtomicPlaneManager planeManager)
    {
        var props = new AtomicPlaneProperties(plane);
        if (!props.IsValid())
        {
            planeManager = null;
            return false;
        }

        planeManager = new AtomicPlaneManager(
            drmDevice,
            plane,
            crtcId,
            props,
            srcWidth,
            srcHeight,
            dstWidth,
            dstHeight,
            logger);
        return true;
    }
}