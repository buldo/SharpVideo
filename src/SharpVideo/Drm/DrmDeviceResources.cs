namespace SharpVideo.Drm;

public class DrmDeviceResources
{
  /// <summary>
  /// Currently allocated framebuffer objects (i.e., objects that can be attached to a given CRTC or sprite for display).
  /// </summary>
  public required IReadOnlyCollection<UInt32> FrameBuffers { get; init; }

  /// <summary>
  /// List the available CRTCs in the configuration.
  /// A CRTC is simply an object that can scan out a framebuffer to a display sink, and contains mode timing and relative position information.
  /// CRTCs drive encoders, which are responsible for converting the pixel stream into a specific display protocol (e.g., MIPI or HDMI).
  /// </summary>
  public required IReadOnlyCollection<UInt32> Crtcs { get; init; }

  /// <summary>
  /// List the available physical connectors on the system.
  /// Note that some of these may not be exposed from the chassis (e.g., LVDS or eDP).
  /// Connectors are attached to encoders and contain information about the attached display sink (e.g., width and height in mm, subpixel ordering, and various other properties).
  /// </summary>
  public required IReadOnlyCollection<UInt32> Connectors { get; init; }

  /// <summary>
  /// List the available encoders on the device.
  /// Each encoder may be associated with a CRTC, and may be used to drive a particular encoder.
  /// </summary>
  public required IReadOnlyCollection<DrmEncoder> Encoders { get; init; }

  public required int MinWidth { get; init; }

  public required int MaxWidth { get; init; }

  public required int MinHeight { get; init; }

  public required int MaxHeight { get; init; }
}