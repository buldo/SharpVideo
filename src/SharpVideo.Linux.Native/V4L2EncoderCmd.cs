using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>v4l2_encoder_cmd</c> structure.
/// Encoder command structure.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct V4L2EncoderCmd
{
    /// <summary>
    /// Encoder command
    /// </summary>
    public uint Cmd;

    /// <summary>
    /// Command flags
    /// </summary>
    public uint Flags;

    /// <summary>
    /// Command-specific data
    /// </summary>
    public fixed uint Raw[8];
}