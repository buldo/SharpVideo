using System.Runtime.Versioning;

using SharpVideo.Drm;

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
    /// <returns>A DualPlaneSelection containing the selected planes and z-position configuration.</returns>
    /// <exception cref="ArgumentNullException">If device is null.</exception>
    /// <exception cref="DrmException">
    /// If no valid plane combination can be found that satisfies all requirements.
    /// </exception>
    public static DualPlaneSelection Select(
        DrmDevice device,
        uint crtcId,
        uint videoFormat,
        uint osdFormat)
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

        // Try to find a valid combination with z-pos support
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
                    return new DualPlaneSelection(videoPlane, osdPlane, zpos);
                }
            }
        }

        // If no z-pos configuration works, try to find any valid pair
        // (assuming hardware default ordering might work)
        foreach (var videoPlane in videoCandidates)
        {
            foreach (var osdPlane in osdCandidates)
            {
                if (videoPlane.Id == osdPlane.Id)
                    continue;

                // Return with zeroed z-pos (hardware defaults)
                return new DualPlaneSelection(
                    videoPlane,
                    osdPlane,
                    new ZPosAssignment(0, 0));
            }
        }

        throw new DrmException(
            $"No valid dual-plane combination found. Video plane candidates: {videoCandidates.Count}, " +
            $"OSD plane candidates: {osdCandidates.Count}. Formats may overlap on single plane.",
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
    /// <returns>True if selection was successful, false otherwise.</returns>
    public static bool TrySelect(
        DrmDevice device,
        uint crtcId,
        uint videoFormat,
        uint osdFormat,
        out DualPlaneSelection selection)
    {
        try
        {
            selection = Select(device, crtcId, videoFormat, osdFormat);
            return true;
        }
        catch (DrmException)
        {
            selection = default;
            return false;
        }
    }
}
