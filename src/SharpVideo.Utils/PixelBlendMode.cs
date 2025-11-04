using System.Runtime.Versioning;

namespace SharpVideo.Utils;

/// <summary>
/// Pixel blend modes supported by DRM for plane composition.
/// </summary>
[SupportedOSPlatform("linux")]
public enum PixelBlendMode : ulong
{
    /// <summary>
    /// No blending - plane is fully opaque.
    /// </summary>
    None = 0,

    /// <summary>
    /// Pre-multiplied alpha blending.
    /// out.rgb = plane.rgb + (1 - plane.alpha) * background.rgb
    /// </summary>
    PreMultiplied = 1,

    /// <summary>
    /// Coverage alpha blending.
    /// out.rgb = plane.rgb * plane.alpha + (1 - plane.alpha) * background.rgb
    /// </summary>
    Coverage = 2
}

/// <summary>
/// Configuration for plane blending and alpha compositing.
/// </summary>
[SupportedOSPlatform("linux")]
public class PlaneBlendConfig
{
    /// <summary>
    /// Blending mode to use for this plane.
    /// </summary>
    public PixelBlendMode BlendMode { get; set; } = PixelBlendMode.PreMultiplied;

    /// <summary>
    /// Global alpha value for the entire plane (0-255).
    /// 0 = fully transparent, 255 = fully opaque.
    /// Combined with per-pixel alpha if format supports it.
    /// </summary>
    public byte GlobalAlpha { get; set; } = 255;

    /// <summary>
    /// Z-order position for this plane.
    /// Lower values are displayed behind higher values.
    /// </summary>
    public ulong ZPosition { get; set; } = 0;
}
