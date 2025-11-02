using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drmModeEncoder</c> structure.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct DrmModeEncoder
{
    /// <summary>
    /// Unique identifier for the encoder.
    /// </summary>
    public readonly uint EncoderId;

    /// <summary>
    /// Type of encoder (e.g., DRM_MODE_ENCODER_DAC, DRM_MODE_ENCODER_TMDS).
    /// </summary>
    public readonly DrmModeEncoderType EncoderType;

    /// <summary>
    /// ID of the CRTC currently connected to the encoder (0 if disconnected).
    /// </summary>
    public readonly uint CrtcId;

    /// <summary>
    /// Bitmask of CRTCs that the encoder can connect to.
    /// </summary>
    public readonly uint PossibleCrtcs;

    /// <summary>
    /// Bitmask of encoders that can be cloned to this encoder.
    /// </summary>
    public readonly uint PossibleClones;
}