using System.Runtime.Versioning;
using Hexa.NET.ImGui;
using SharpVideo.Drm;
using SharpVideo.Gbm;

namespace SharpVideo.ImGui;

/// <summary>
/// Configuration for ImGui integration with DRM/KMS rendering.
/// Specifies where and how ImGui should be rendered on the display.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class ImGuiDrmConfiguration
{
    /// <summary>
    /// Width of the rendering surface in pixels.
    /// </summary>
    public required uint Width { get; init; }

    /// <summary>
    /// Height of the rendering surface in pixels.
    /// </summary>
    public required uint Height { get; init; }

    /// <summary>
    /// DRM device to use for rendering. The library does not own this device.
    /// </summary>
    public required DrmDevice DrmDevice { get; init; }

    /// <summary>
    /// GBM device to use for buffer management. The library does not own this device.
    /// </summary>
    public required GbmDevice GbmDevice { get; init; }

    /// <summary>
    /// Native handle to the GBM surface where ImGui will render.
    /// This should be obtained from your DRM plane presenter.
    /// </summary>
    public required nint GbmSurfaceHandle { get; init; }

    /// <summary>
    /// Pixel format for the rendering surface.
    /// Default: ARGB8888 (with alpha for transparency/compositing).
    /// </summary>
    public PixelFormat PixelFormat { get; init; } = KnownPixelFormats.DRM_FORMAT_ARGB8888;

    /// <summary>
    /// ImGui configuration flags.
    /// Default: NavEnableKeyboard | DockingEnable
    /// </summary>
    public ImGuiConfigFlags ConfigFlags { get; init; } = 
        ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.DockingEnable;

    /// <summary>
    /// Whether ImGui should draw its own cursor.
    /// Default: true (recommended for DRM/KMS without windowing system)
    /// </summary>
    public bool DrawCursor { get; init; } = true;

    /// <summary>
    /// Scale factor for UI elements (1.0 = normal).
    /// Default: 1.0
    /// </summary>
    public float UiScale { get; init; } = 1.0f;

    /// <summary>
    /// OpenGL ES version string for shader compilation.
    /// Default: "#version 300 es" (OpenGL ES 3.0)
    /// </summary>
    public string GlslVersion { get; init; } = "#version 300 es";

    /// <summary>
    /// Whether to enable input handling via libinput.
    /// If true, InputManager must be provided to ImGuiManager.
    /// Default: true
    /// </summary>
    public bool EnableInput { get; init; } = true;

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    internal void Validate()
    {
        if (Width == 0 || Height == 0)
        {
            throw new ArgumentException("Width and Height must be greater than zero");
        }

        if (DrmDevice == null)
        {
            throw new ArgumentNullException(nameof(DrmDevice));
        }

        if (GbmDevice == null)
        {
            throw new ArgumentNullException(nameof(GbmDevice));
        }

        if (GbmSurfaceHandle == 0)
        {
            throw new ArgumentException("GbmSurfaceHandle must be a valid handle", nameof(GbmSurfaceHandle));
        }

        if (UiScale <= 0)
        {
            throw new ArgumentException("UiScale must be greater than zero", nameof(UiScale));
        }
    }
}
