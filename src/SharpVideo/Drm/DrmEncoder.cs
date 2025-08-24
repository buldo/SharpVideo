using SharpVideo.Linux.Native;

namespace SharpVideo.Drm;

public readonly struct DrmEncoder
{
  /// <summary>
  /// Unique identifier for the encoder.
  /// </summary>
  public required uint EncoderId { get; init; }

  /// <summary>
  /// Type of encoder (e.g., DRM_MODE_ENCODER_DAC, DRM_MODE_ENCODER_TMDS).
  /// </summary>
  public required DrmModeEncoderType EncoderType { get; init; }

  /// <summary>
  /// ID of the CRTC currently connected to the encoder (0 if disconnected).
  /// </summary>
  public required uint CrtcId { get; init; }

  /// <summary>
  /// Bitmask of CRTCs that the encoder can connect to.
  /// </summary>
  public required uint PossibleCrtcs { get; init; }

  /// <summary>
  /// Bitmask of encoders that can be cloned to this encoder.
  /// </summary>
  public required uint PossibleClones { get; init; }
}