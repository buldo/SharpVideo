using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// Managed representation of the native <c>v4l2_decoder_cmd</c> structure.
/// Decoder command structure.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct V4L2DecoderCmd
{
    /// <summary>
    /// Decoder command
    /// </summary>
    public uint Cmd;

    /// <summary>
    /// Command flags
    /// </summary>
    public uint Flags;

    /// <summary>
    /// Union data - using largest member (raw data)
    /// Contains stop.pts (8 bytes), start.speed+format (8 bytes), or raw.data (64 bytes)
    /// </summary>
    public fixed uint Raw[16]; // 16 * 4 = 64 bytes
}