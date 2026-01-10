using System.Runtime.Versioning;

using SharpVideo.Drm;

namespace SharpVideo.Utils;

/// <summary>
/// Represents atomic plane properties required for atomic modesetting.
/// Separates mandatory properties (required for basic operation) from optional properties (feature enhancements).
/// </summary>
[SupportedOSPlatform("linux")]
public class AtomicPlaneProperties
{
    public AtomicPlaneProperties(DrmPlane plane)
    {
        var props = plane.GetProperties();
        
        // Mandatory properties for atomic modesetting
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
        
        // Optional properties for alpha blending/compositing
        PixelBlendModePropertyId = GetPropId("pixel blend mode");
        ZposPropertyId = GetPropId("zpos");
        AlphaPropertyId = GetPropId("alpha");

        uint GetPropId(string name)
        {
            return props.FirstOrDefault(p => p.Name == name)?.Id ?? 0;
        }
    }

    // -------------------- Mandatory Properties --------------------
    
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

    // -------------------- Optional Properties --------------------
    
    /// <summary>
    /// Optional property for configuring alpha blending mode.
    /// Not all hardware supports this property - use HasPixelBlendMode() to check availability.
    /// </summary>
    public uint PixelBlendModePropertyId { get; }
    
    /// <summary>
    /// Optional property for configuring z-position (layer ordering).
    /// Not all hardware supports this property - use HasZpos() to check availability.
    /// </summary>
    public uint ZposPropertyId { get; }
    
    /// <summary>
    /// Optional property for configuring global alpha (transparency).
    /// Not all hardware supports this property - use HasAlpha() to check availability.
    /// </summary>
    public uint AlphaPropertyId { get; }

    // -------------------- Validation Methods --------------------
    
    /// <summary>
    /// Checks if all mandatory atomic properties are available.
    /// Does NOT check optional properties like PixelBlendMode, Zpos, or Alpha.
    /// </summary>
    /// <returns>True if all mandatory properties are present, false otherwise</returns>
    public bool IsValid()
    {
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
    /// This is an optional feature - the plane can still work without it.
    /// </summary>
    public bool HasPixelBlendMode() => PixelBlendModePropertyId != 0;
    
    /// <summary>
    /// Checks if zpos property is available for layer ordering.
    /// This is an optional feature - the plane can still work without it.
    /// </summary>
    public bool HasZpos() => ZposPropertyId != 0;
    
    /// <summary>
    /// Checks if alpha property is available for global transparency.
    /// This is an optional feature - the plane can still work without it.
    /// </summary>
    public bool HasAlpha() => AlphaPropertyId != 0;
}