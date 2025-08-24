using SharpVideo.Linux.Native;

namespace SharpVideo.Drm;

public class DrmModeInfo
{
    /// <summary>
    /// Pixel clock in kHz.
    /// </summary>
    public required uint Clock { get; init; }

    /// <summary>
    /// Horizontal display size.
    /// </summary>
    public required ushort HDisplay { get; init; }

    /// <summary>
    /// Horizontal sync start.
    /// </summary>
    public required ushort HSyncStart { get; init; }

    /// <summary>
    /// Horizontal sync end.
    /// </summary>
    public required ushort HSyncEnd { get; init; }

    /// <summary>
    /// Horizontal total.
    /// </summary>
    public required ushort HTotal { get; init; }

    /// <summary>
    /// Horizontal skew.
    /// </summary>
    public required ushort HSkew { get; init; }

    /// <summary>
    /// Vertical display size.
    /// </summary>
    public required ushort VDisplay { get; init; }

    /// <summary>
    /// Vertical sync start.
    /// </summary>
    public required ushort VSyncStart { get; init; }

    /// <summary>
    /// Vertical sync end.
    /// </summary>
    public required ushort VSyncEnd { get; init; }

    /// <summary>
    /// Vertical total.
    /// </summary>
    public required ushort VTotal { get; init; }

    /// <summary>
    /// Vertical scan.
    /// </summary>
    public required ushort VScan { get; init; }

    /// <summary>
    /// Vertical refresh rate in Hz.
    /// </summary>
    public required uint VRefresh { get; init; }

    /// <summary>
    /// Mode flags.
    /// </summary>
    public required DrmModeFlag Flags { get; init; }

    /// <summary>
    /// Mode type.
    /// </summary>
    public required DrmModeType Type { get; init; }

    /// <summary>
    /// Mode name (32 characters).
    /// </summary>
    public required string Name { get; init; }

}