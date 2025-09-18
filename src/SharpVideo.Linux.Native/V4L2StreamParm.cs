using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>v4l2_fract</c> structure.
/// Represents a fraction.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct V4L2Fract
{
    /// <summary>
    /// Numerator
    /// </summary>
    public uint Numerator;

    /// <summary>
    /// Denominator
    /// </summary>
    public uint Denominator;
}

/// <summary>
/// Managed representation of the native <c>v4l2_captureparm</c> structure.
/// Capture parameters.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct V4L2CaptureParm
{
    /// <summary>
    /// Capture capability flags
    /// </summary>
    public uint Capability;

    /// <summary>
    /// Capture mode flags
    /// </summary>
    public uint CaptureMode;

    /// <summary>
    /// Time per frame in seconds
    /// </summary>
    public V4L2Fract TimePerFrame;

    /// <summary>
    /// Extended flags
    /// </summary>
    public uint ExtendedMode;

    /// <summary>
    /// # of buffers for read
    /// </summary>
    public uint ReadBuffers;

    /// <summary>
    /// Reserved for future extensions
    /// </summary>
    public fixed uint Reserved[4];
}

/// <summary>
/// Managed representation of the native <c>v4l2_outputparm</c> structure.
/// Output parameters.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct V4L2OutputParm
{
    /// <summary>
    /// Output capability flags
    /// </summary>
    public uint Capability;

    /// <summary>
    /// Output mode flags
    /// </summary>
    public uint OutputMode;

    /// <summary>
    /// Time per frame in seconds
    /// </summary>
    public V4L2Fract TimePerFrame;

    /// <summary>
    /// Extended flags
    /// </summary>
    public uint ExtendedMode;

    /// <summary>
    /// # of buffers for write
    /// </summary>
    public uint WriteBuffers;

    /// <summary>
    /// Reserved for future extensions
    /// </summary>
    public fixed uint Reserved[4];
}

/// <summary>
/// Managed representation of the native <c>v4l2_streamparm</c> structure.
/// Stream parameters.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct V4L2StreamParm
{
    /// <summary>
    /// Stream type
    /// </summary>
    public uint Type;

    /// <summary>
    /// Capture parameters (when Type is capture)
    /// </summary>
    public V4L2CaptureParm Capture;

    // Note: In the actual C union, this would share memory with Capture
    // For simplicity in C#, we'll just use Capture for capture types
    // and handle output separately if needed
}

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