using System.Runtime.Versioning;

using SharpVideo.Drm;

namespace SharpVideo.Utils;

/// <summary>
/// Represents the computed z-position values for dual-plane configuration.
/// OsdZPos will always be greater than VideoZPos.
/// </summary>
/// <param name="VideoZPos">The z-position value for the video plane (lower/behind).</param>
/// <param name="OsdZPos">The z-position value for the OSD plane (higher/on top).</param>
public readonly record struct ZPosAssignment(uint VideoZPos, uint OsdZPos);

/// <summary>
/// Resolves z-position values for dual-plane configurations ensuring proper layer ordering.
/// OSD plane is always positioned above the video plane.
/// </summary>
[SupportedOSPlatform("linux")]
public static class ZPosResolver
{
    /// <summary>
    /// Calculates z-position values ensuring OSD is rendered above video.
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

        var videoZPosRange = videoPlane.GetPlaneZPositionRange();
        var osdZPosRange = osdPlane.GetPlaneZPositionRange();

        // Case 1: Neither plane supports zpos - rely on hardware defaults
        if (!videoZPosRange.HasValue && !osdZPosRange.HasValue)
        {
            throw new InvalidOperationException(
                $"Neither plane supports zpos property. Video plane {videoPlane.Id} and OSD plane {osdPlane.Id} " +
                "cannot have their z-ordering configured. Use planes with zpos support or ensure hardware default ordering is correct.");
        }

        // Case 2: Only video plane supports zpos
        if (videoZPosRange.HasValue && !osdZPosRange.HasValue)
        {
            // Set video to minimum zpos, OSD will use its fixed position
            return new ZPosAssignment(
                VideoZPos: (uint)videoZPosRange.Value.min,
                OsdZPos: 0 // OSD doesn't support zpos, use 0 as placeholder
            );
        }

        // Case 3: Only OSD plane supports zpos
        if (!videoZPosRange.HasValue && osdZPosRange.HasValue)
        {
            // Set OSD to maximum zpos, video will use its fixed position
            return new ZPosAssignment(
                VideoZPos: 0, // Video doesn't support zpos, use 0 as placeholder
                OsdZPos: (uint)osdZPosRange.Value.max
            );
        }

        // Case 4: Both planes support zpos
        var videoMin = videoZPosRange!.Value.min;
        var videoMax = videoZPosRange.Value.max;
        var osdMin = osdZPosRange!.Value.min;
        var osdMax = osdZPosRange.Value.max;

        // Try to find values where OsdZPos > VideoZPos
        // Strategy: Use video's minimum and OSD's maximum to maximize separation
        uint videoZPos = (uint)videoMin;
        uint osdZPos = (uint)osdMax;

        // Check if this gives us valid ordering
        if (osdZPos > videoZPos)
        {
            return new ZPosAssignment(VideoZPos: videoZPos, OsdZPos: osdZPos);
        }

        // If min/max doesn't work, try other combinations
        // Try video at min, OSD at max
        if (osdMax > videoMin)
        {
            return new ZPosAssignment(VideoZPos: (uint)videoMin, OsdZPos: (uint)osdMax);
        }

        // Try finding any valid combination where OSD > Video
        for (uint v = (uint)videoMin; v <= videoMax; v++)
        {
            for (uint o = (uint)osdMin; o <= osdMax; o++)
            {
                if (o > v)
                {
                    return new ZPosAssignment(VideoZPos: v, OsdZPos: o);
                }
            }
        }

        // No valid ordering possible
        throw new InvalidOperationException(
            $"Cannot configure z-ordering: Video plane {videoPlane.Id} (zpos range {videoMin}-{videoMax}) " +
            $"and OSD plane {osdPlane.Id} (zpos range {osdMin}-{osdMax}) have no valid configuration " +
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
