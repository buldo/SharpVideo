using System.Runtime.Versioning;

using SharpVideo.Drm;
using SharpVideo.Linux.Native.Drm;

namespace SharpVideo.Utils;

/// <summary>
/// Represents the computed z-position values for dual-plane configuration.
/// OsdZPos will always be greater than VideoZPos.
/// </summary>
/// <param name="VideoZPos">The z-position value for the video plane (lower/behind).</param>
/// <param name="OsdZPos">The z-position value for the OSD plane (higher/on top).</param>
/// <param name="VideoZPosImmutable">Whether the video plane's zpos is immutable (cannot be changed).</param>
/// <param name="OsdZPosImmutable">Whether the OSD plane's zpos is immutable (cannot be changed).</param>
public readonly record struct ZPosAssignment(uint VideoZPos, uint OsdZPos, bool VideoZPosImmutable, bool OsdZPosImmutable)
{
    /// <summary>
    /// Creates a ZPosAssignment with mutable zpos values.
    /// </summary>
    public ZPosAssignment(uint videoZPos, uint osdZPos) : this(videoZPos, osdZPos, false, false) { }
}

/// <summary>
/// Resolves z-position values for dual-plane configurations ensuring proper layer ordering.
/// OSD plane is always positioned above the video plane.
/// </summary>
[SupportedOSPlatform("linux")]
public static class ZPosResolver
{
    /// <summary>
    /// Gets current zpos value and whether it's immutable for a plane.
    /// </summary>
    public static (ulong current, bool immutable)? GetZPosInfo(DrmPlane plane)
    {
        ArgumentNullException.ThrowIfNull(plane);

        var props = plane.GetProperties();
        var zposProp = props.FirstOrDefault(p => p.Name.Equals("zpos", StringComparison.OrdinalIgnoreCase));

        if (zposProp == null)
            return null;

        var isImmutable = (zposProp.Flags & (uint)PropertyType.DRM_MODE_PROP_IMMUTABLE) != 0;
        return (zposProp.Value, isImmutable);
    }

    /// <summary>
    /// Calculates z-position values ensuring OSD is rendered above video.
    /// Takes into account immutable zpos properties.
    /// </summary>
    /// <param name="videoPlane">The plane to be used for video rendering.</param>
    /// <param name="osdPlane">The plane to be used for OSD/UI rendering.</param>
    /// <returns>A ZPosAssignment with video below and OSD above.</returns>
    /// <exception cref="ArgumentNullException">If either plane is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// If no valid z-position ordering is possible (e.g., neither plane supports zpos,
    /// or the ranges don't allow OSD to be above video).
    /// </exception>
    public static ZPosAssignment CalculateZPosValues(DrmPlane videoPlane, DrmPlane osdPlane)
    {
        ArgumentNullException.ThrowIfNull(videoPlane);
        ArgumentNullException.ThrowIfNull(osdPlane);

        var videoZPosInfo = GetZPosInfo(videoPlane);
        var osdZPosInfo = GetZPosInfo(osdPlane);
        var videoZPosRange = videoPlane.GetPlaneZPositionRange();
        var osdZPosRange = osdPlane.GetPlaneZPositionRange();

        // Case 1: Neither plane supports zpos - rely on hardware defaults
        if (!videoZPosInfo.HasValue && !osdZPosInfo.HasValue)
        {
            throw new InvalidOperationException(
                $"Neither plane supports zpos property. Video plane {videoPlane.Id} and OSD plane {osdPlane.Id} " +
                "cannot have their z-ordering configured. Use planes with zpos support or ensure hardware default ordering is correct.");
        }

        var videoImmutable = videoZPosInfo?.immutable ?? true;
        var osdImmutable = osdZPosInfo?.immutable ?? true;

        // Case 2: Both zpos are immutable - check if current values give correct ordering
        if (videoImmutable && osdImmutable)
        {
            var videoCurrentZPos = videoZPosInfo?.current ?? 0;
            var osdCurrentZPos = osdZPosInfo?.current ?? 0;

            if (osdCurrentZPos > videoCurrentZPos)
            {
                // Good - OSD is already above video
                return new ZPosAssignment(
                    (uint)videoCurrentZPos,
                    (uint)osdCurrentZPos,
                    videoImmutable,
                    osdImmutable);
            }

            throw new InvalidOperationException(
                $"Both planes have immutable zpos but OSD (zpos={osdCurrentZPos}) is not above video (zpos={videoCurrentZPos}). " +
                $"Video plane {videoPlane.Id}, OSD plane {osdPlane.Id}.");
        }

        // Case 3: Video immutable, OSD mutable - set OSD above video's fixed position
        if (videoImmutable && !osdImmutable)
        {
            var videoCurrentZPos = (uint)(videoZPosInfo?.current ?? 0);
            var osdMax = (uint)(osdZPosRange?.max ?? 255);
            var osdZPos = Math.Max(videoCurrentZPos + 1, osdMax);

            return new ZPosAssignment(videoCurrentZPos, osdZPos, true, false);
        }

        // Case 4: Video mutable, OSD immutable - set video below OSD's fixed position
        if (!videoImmutable && osdImmutable)
        {
            var osdCurrentZPos = (uint)(osdZPosInfo?.current ?? 0);
            var videoMin = (uint)(videoZPosRange?.min ?? 0);
            var videoZPos = osdCurrentZPos > 0 ? Math.Min(osdCurrentZPos - 1, videoMin) : 0;

            if (videoZPos >= osdCurrentZPos)
            {
                throw new InvalidOperationException(
                    $"Cannot configure z-ordering: OSD plane {osdPlane.Id} has immutable zpos={osdCurrentZPos}, " +
                    $"but video plane {videoPlane.Id} cannot be set below it (min={videoMin}).");
            }

            return new ZPosAssignment(videoZPos, osdCurrentZPos, false, true);
        }

        // Case 5: Both mutable - use video's minimum and OSD's maximum
        var vMin = (uint)(videoZPosRange?.min ?? 0);
        var vMax = (uint)(videoZPosRange?.max ?? 255);
        var oMin = (uint)(osdZPosRange?.min ?? 0);
        var oMax = (uint)(osdZPosRange?.max ?? 255);

        // Try video at min, OSD at max
        if (oMax > vMin)
        {
            return new ZPosAssignment(vMin, oMax, false, false);
        }

        // Try finding any valid combination where OSD > Video
        for (uint v = vMin; v <= vMax; v++)
        {
            for (uint o = oMin; o <= oMax; o++)
            {
                if (o > v)
                {
                    return new ZPosAssignment(v, o, false, false);
                }
            }
        }

        // No valid ordering possible
        throw new InvalidOperationException(
            $"Cannot configure z-ordering: Video plane {videoPlane.Id} (zpos range {vMin}-{vMax}) " +
            $"and OSD plane {osdPlane.Id} (zpos range {oMin}-{oMax}) have no valid configuration " +
            "where OSD can be above video.");
    }

    /// <summary>
    /// Checks if z-position configuration is possible for the given planes.
    /// </summary>
    /// <param name="videoPlane">The plane to be used for video rendering.</param>
    /// <param name="osdPlane">The plane to be used for OSD/UI rendering.</param>
    /// <returns>True if valid z-ordering can be configured, false otherwise.</returns>
    public static bool CanConfigureZPos(DrmPlane videoPlane, DrmPlane osdPlane)
    {
        ArgumentNullException.ThrowIfNull(videoPlane);
        ArgumentNullException.ThrowIfNull(osdPlane);

        try
        {
            CalculateZPosValues(videoPlane, osdPlane);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
