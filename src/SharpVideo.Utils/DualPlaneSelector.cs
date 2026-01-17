using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Drm;
using SharpVideo.Linux.Native;

namespace SharpVideo.Utils;

/// <summary>
/// Represents a successfully selected dual-plane configuration.
/// </summary>
/// <param name="VideoPlane">The DRM plane selected for video rendering.</param>
/// <param name="OsdPlane">The DRM plane selected for OSD/UI rendering.</param>
/// <param name="ZPos">The computed z-position values for proper layer ordering.</param>
public readonly record struct DualPlaneSelection(
    DrmPlane VideoPlane,
    DrmPlane OsdPlane,
    ZPosAssignment ZPos);

/// <summary>
/// Selects compatible planes for dual-plane video and OSD rendering.
/// </summary>
[SupportedOSPlatform("linux")]
public static class DualPlaneSelector
{
    /// <summary>
    /// Selects two compatible planes for video and OSD rendering.
    /// </summary>
    /// <param name="device">The DRM device to query for planes.</param>
    /// <param name="crtcId">The CRTC ID the planes must be compatible with.</param>
    /// <param name="videoFormat">The pixel format required for the video plane.</param>
    /// <param name="osdFormat">The pixel format required for the OSD plane.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    /// <returns>A DualPlaneSelection containing the selected planes and z-position configuration.</returns>
    /// <exception cref="ArgumentNullException">If device is null.</exception>
    /// <exception cref="DrmException">
    /// If no valid plane combination can be found that satisfies all requirements.
    /// </exception>
    public static DualPlaneSelection Select(
        DrmDevice device,
        uint crtcId,
        uint videoFormat,
        uint osdFormat,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);

        var resources = device.GetResources()
            ?? throw new DrmException("Failed to get DRM resources", device.DeviceFd);

        // Find the CRTC index for filtering planes by PossibleCrtcs mask
        var crtcList = resources.Crtcs.ToList();
        var crtcIndex = crtcList.IndexOf(crtcId);
        if (crtcIndex < 0)
        {
            throw new DrmException($"CRTC {crtcId} not found in device resources", device.DeviceFd);
        }

        var crtcMask = 1u << crtcIndex;

        // Filter planes compatible with this CRTC
        var compatiblePlanes = resources.Planes
            .Where(p => (p.PossibleCrtcs & crtcMask) != 0)
            .ToList();

        if (compatiblePlanes.Count < 2)
        {
            throw new DrmException(
                $"Not enough planes compatible with CRTC {crtcId}. Found {compatiblePlanes.Count}, need at least 2.",
                device.DeviceFd);
        }

        // Log available planes and their zpos info
        foreach (var plane in compatiblePlanes)
        {
            var zposInfo = ZPosResolver.GetZPosInfo(plane);
            var zposRange = plane.GetPlaneZPositionRange();
            var formatNames = string.Join(", ", plane.Formats.Select(f => FourCC.ToString(f)));

            if (zposInfo.HasValue)
            {
                logger?.LogDebug(
                    "Plane {PlaneId}: zpos={ZPos} (immutable={Immutable}, range={Min}-{Max}), formats: {Formats}",
                    plane.Id, zposInfo.Value.current, zposInfo.Value.immutable,
                    zposRange?.min ?? 0, zposRange?.max ?? 0,
                    formatNames);
            }
            else
            {
                logger?.LogDebug("Plane {PlaneId}: no zpos support, formats: {Formats}", plane.Id, formatNames);
            }
        }

        // Find planes supporting each format
        var videoCandidates = compatiblePlanes
            .Where(p => p.Formats.Contains(videoFormat))
            .ToList();

        var osdCandidates = compatiblePlanes
            .Where(p => p.Formats.Contains(osdFormat))
            .ToList();

        if (videoCandidates.Count == 0)
        {
            throw new DrmPlaneNotFoundException("Video", new PixelFormat(videoFormat), device.DeviceFd);
        }

        if (osdCandidates.Count == 0)
        {
            throw new DrmPlaneNotFoundException("OSD", new PixelFormat(osdFormat), device.DeviceFd);
        }

        logger?.LogDebug("Video format candidates: {Count}, OSD format candidates: {Count2}",
            videoCandidates.Count, osdCandidates.Count);

        // First pass: Try to find planes where zpos can be configured correctly
        foreach (var videoPlane in videoCandidates)
        {
            foreach (var osdPlane in osdCandidates)
            {
                // Must be different planes
                if (videoPlane.Id == osdPlane.Id)
                    continue;

                // Try to configure z-ordering
                if (ZPosResolver.CanConfigureZPos(videoPlane, osdPlane))
                {
                    var zpos = ZPosResolver.CalculateZPosValues(videoPlane, osdPlane);
                    logger?.LogInformation(
                        "Selected planes - Video: {VideoId} (zpos={VZ}, immutable={VI}), OSD: {OsdId} (zpos={OZ}, immutable={OI})",
                        videoPlane.Id, zpos.VideoZPos, zpos.VideoZPosImmutable,
                        osdPlane.Id, zpos.OsdZPos, zpos.OsdZPosImmutable);
                    return new DualPlaneSelection(videoPlane, osdPlane, zpos);
                }
            }
        }

        // Second pass: Check if any pair has OSD with higher current zpos than video (for immutable cases)
        foreach (var videoPlane in videoCandidates)
        {
            var videoZposInfo = ZPosResolver.GetZPosInfo(videoPlane);

            foreach (var osdPlane in osdCandidates)
            {
                if (videoPlane.Id == osdPlane.Id)
                    continue;

                var osdZposInfo = ZPosResolver.GetZPosInfo(osdPlane);

                // If both have zpos and OSD is already above video, use them
                if (videoZposInfo.HasValue && osdZposInfo.HasValue)
                {
                    if (osdZposInfo.Value.current > videoZposInfo.Value.current)
                    {
                        var zpos = new ZPosAssignment(
                            (uint)videoZposInfo.Value.current,
                            (uint)osdZposInfo.Value.current,
                            videoZposInfo.Value.immutable,
                            osdZposInfo.Value.immutable);

                        logger?.LogInformation(
                            "Selected planes (by current zpos) - Video: {VideoId} (zpos={VZ}), OSD: {OsdId} (zpos={OZ})",
                            videoPlane.Id, zpos.VideoZPos, osdPlane.Id, zpos.OsdZPos);
                        return new DualPlaneSelection(videoPlane, osdPlane, zpos);
                    }
                }
            }
        }

        // Third pass: Try swapping video/osd assignment if format support allows
        // (maybe one plane supports both formats)
        foreach (var plane1 in compatiblePlanes)
        {
            foreach (var plane2 in compatiblePlanes)
            {
                if (plane1.Id == plane2.Id)
                    continue;

                // Check if we can swap: plane1 for OSD, plane2 for video
                bool plane1SupportsOsd = plane1.Formats.Contains(osdFormat);
                bool plane2SupportsVideo = plane2.Formats.Contains(videoFormat);

                if (plane1SupportsOsd && plane2SupportsVideo)
                {
                    var zposInfo1 = ZPosResolver.GetZPosInfo(plane1);
                    var zposInfo2 = ZPosResolver.GetZPosInfo(plane2);

                    if (zposInfo1.HasValue && zposInfo2.HasValue)
                    {
                        // plane2 = video (should be lower), plane1 = OSD (should be higher)
                        if (zposInfo1.Value.current > zposInfo2.Value.current)
                        {
                            var zpos = new ZPosAssignment(
                                (uint)zposInfo2.Value.current,
                                (uint)zposInfo1.Value.current,
                                zposInfo2.Value.immutable,
                                zposInfo1.Value.immutable);

                            logger?.LogInformation(
                                "Selected planes (swapped by zpos) - Video: {VideoId} (zpos={VZ}), OSD: {OsdId} (zpos={OZ})",
                                plane2.Id, zpos.VideoZPos, plane1.Id, zpos.OsdZPos);
                            return new DualPlaneSelection(plane2, plane1, zpos);
                        }
                    }
                }
            }
        }

        throw new DrmException(
            $"No valid dual-plane combination found. Video plane candidates: {videoCandidates.Count}, " +
            $"OSD plane candidates: {osdCandidates.Count}. Could not configure z-ordering for OSD above video.",
            device.DeviceFd);
    }

    /// <summary>
    /// Attempts to select compatible planes for dual-plane rendering.
    /// </summary>
    /// <param name="device">The DRM device to query for planes.</param>
    /// <param name="crtcId">The CRTC ID the planes must be compatible with.</param>
    /// <param name="videoFormat">The pixel format required for the video plane.</param>
    /// <param name="osdFormat">The pixel format required for the OSD plane.</param>
    /// <param name="selection">The selected planes if successful.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    /// <returns>True if selection was successful, false otherwise.</returns>
    public static bool TrySelect(
        DrmDevice device,
        uint crtcId,
        uint videoFormat,
        uint osdFormat,
        out DualPlaneSelection selection,
        ILogger? logger = null)
    {
        try
        {
            selection = Select(device, crtcId, videoFormat, osdFormat, logger);
            return true;
        }
        catch (DrmException)
        {
            selection = default;
            return false;
        }
    }
}
