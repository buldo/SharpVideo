using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drmModePropertyBlobRes</c> structure.
/// Contains property blob information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct DrmModePropertyBlobRes
{
    /// <summary>
    /// Property blob ID.
    /// </summary>
    public readonly uint Id;

    /// <summary>
    /// Length of the blob data in bytes.
    /// </summary>
    public readonly uint Length;

    /// <summary>
    /// Pointer to the blob data.
    /// </summary>
    private readonly void* _data;

    /// <summary>
    /// Gets a span over the blob data.
    /// </summary>
    public ReadOnlySpan<byte> Data => _data == null
        ? ReadOnlySpan<byte>.Empty
        : new ReadOnlySpan<byte>(_data, (int)Length);
}