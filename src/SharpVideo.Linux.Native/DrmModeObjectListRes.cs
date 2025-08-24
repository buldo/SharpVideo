using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drmModeObjectListRes</c> structure.
/// Contains object list information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct DrmModeObjectListRes
{
    /// <summary>
    /// Number of objects.
    /// </summary>
    public readonly uint Count;

    /// <summary>
    /// First element of the objects array (flexible array member).
    /// </summary>
    private readonly uint _firstObject;

    /// <summary>
    /// Gets a span over the object IDs.
    /// Note: This uses unsafe pointer arithmetic to access the flexible array member.
    /// </summary>
    public ReadOnlySpan<uint> Objects
    {
        get
        {
            if (Count == 0) return ReadOnlySpan<uint>.Empty;
            
            fixed (uint* ptr = &_firstObject)
            {
                return new ReadOnlySpan<uint>(ptr, (int)Count);
            }
        }
    }
}