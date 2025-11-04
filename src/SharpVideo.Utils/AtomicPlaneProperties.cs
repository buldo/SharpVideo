using System.Runtime.Versioning;

using SharpVideo.Drm;

namespace SharpVideo.Utils;

[SupportedOSPlatform("linux")]
public class AtomicPlaneProperties
{
    public AtomicPlaneProperties(DrmPlane plane)
    {
        var props = plane.GetProperties();
        FbIdPropertyId = GetPropId("FB_ID");
        CrtcIdPropertyId = GetPropId("CRTC_ID");
        CrtcXPropertyId = GetPropId("CRTC_X");
        CrtcYPropertyId = GetPropId("CRTC_Y");
        CrtcWPropertyId = GetPropId("CRTC_W");
        CrtcHPropertyId = GetPropId("CRTC_H");
        SrcXPropertyId = GetPropId("SRC_X");
        SrcYPropertyId = GetPropId("SRC_Y");
        SrcWPropertyId = GetPropId("SRC_W");
        SrcHPropertyId = GetPropId("SRC_H");
        PixelBlendModePropertyId = GetPropId("pixel blend mode");

        uint GetPropId(string name)
        {
            return props.FirstOrDefault(p => p.Name == name)?.Id ?? 0;
        }
    }

    public uint FbIdPropertyId { get; }
    public uint CrtcIdPropertyId { get; }
    public uint CrtcXPropertyId { get; }
    public uint CrtcYPropertyId { get; }
    public uint CrtcWPropertyId { get; }
    public uint CrtcHPropertyId { get; }
    public uint SrcXPropertyId { get; }
    public uint SrcYPropertyId { get; }
    public uint SrcWPropertyId { get; }
    public uint SrcHPropertyId { get; }

    /// <summary>
    /// Optional property for configuring alpha blending mode.
    /// Not all hardware supports this property.
    /// </summary>
    public uint PixelBlendModePropertyId { get; }

    /// <summary>
    /// Checks if all mandatory atomic properties are available.
    /// </summary>
    public bool IsValid()
    {
        // Pixel blend mode is optional, so not checked here
        if (FbIdPropertyId == 0 || CrtcIdPropertyId == 0 ||
            CrtcXPropertyId == 0 || CrtcYPropertyId == 0 ||
            CrtcWPropertyId == 0 || CrtcHPropertyId == 0 ||
            SrcXPropertyId == 0 || SrcYPropertyId == 0 ||
            SrcWPropertyId == 0 || SrcHPropertyId == 0)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if pixel blend mode property is available for configuring transparency.
    /// </summary>
    public bool HasPixelBlendMode() => PixelBlendModePropertyId != 0;
}