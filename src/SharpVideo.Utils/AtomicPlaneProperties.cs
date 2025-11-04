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

    // -------------------- Validation Methods --------------------
    
    /// <summary>
    /// Checks if all mandatory atomic properties are available.
    /// Does NOT check optional properties like PixelBlendMode.
    /// </summary>
    /// <returns>True if all mandatory properties are present, false otherwise</returns>
    public bool IsValid()
    {
        // Note: PixelBlendMode is optional and NOT checked here
        // Use HasPixelBlendMode() to check for blend support separately
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
    /// <returns>True if pixel blend mode is supported, false otherwise</returns>
    public bool HasPixelBlendMode() => PixelBlendModePropertyId != 0;
}