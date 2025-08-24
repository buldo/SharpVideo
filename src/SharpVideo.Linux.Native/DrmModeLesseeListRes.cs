using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drmModeLesseeListRes</c> structure.
/// Contains lessee list information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct DrmModeLesseeListRes
{
    /// <summary>
    /// Number of lessees.
    /// </summary>
    public readonly uint Count;

    /// <summary>
    /// First element of the lessees array (flexible array member).
    /// </summary>
    private readonly uint _firstLessee;

    /// <summary>
    /// Gets a span over the lessee IDs.
    /// Note: This uses unsafe pointer arithmetic to access the flexible array member.
    /// </summary>
    public ReadOnlySpan<uint> Lessees
    {
        get
        {
            if (Count == 0) return ReadOnlySpan<uint>.Empty;
            
            fixed (uint* ptr = &_firstLessee)
            {
                return new ReadOnlySpan<uint>(ptr, (int)Count);
            }
        }
    }
}