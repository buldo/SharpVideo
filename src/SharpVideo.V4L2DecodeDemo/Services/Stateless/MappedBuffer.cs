using System.Runtime.Versioning;

namespace SharpVideo.V4L2DecodeDemo.Services.Stateless;

/// <summary>
/// Represents a mapped V4L2 buffer for stateless decoding operations
/// </summary>
[SupportedOSPlatform("linux")]
public class MappedBuffer
{
    /// <summary>
    /// Buffer index in the V4L2 queue
    /// </summary>
    public required uint Index { get; init; }

    /// <summary>
    /// Pointer to the mapped memory region
    /// </summary>
    public required IntPtr Pointer { get; init; }

    /// <summary>
    /// Size of the mapped buffer in bytes
    /// </summary>
    public required uint Size { get; init; }

    /// <summary>
    /// V4L2 planes associated with this buffer
    /// </summary>
    public required SharpVideo.Linux.Native.V4L2Plane[] Planes { get; init; }
}