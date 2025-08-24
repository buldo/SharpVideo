using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drmModeObjectProperties</c> structure.
/// Contains object properties information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe readonly struct DrmModeObjectProperties
{
    /// <summary>
    /// Number of properties.
    /// </summary>
    public readonly uint CountProps;

    /// <summary>
    /// Pointer to property IDs array.
    /// </summary>
    private readonly uint* _props;

    /// <summary>
    /// Pointer to property values array.
    /// </summary>
    private readonly ulong* _propValues;

    /// <summary>
    /// Gets a span over the property IDs.
    /// </summary>
    public ReadOnlySpan<uint> Props => _props == null
        ? ReadOnlySpan<uint>.Empty
        : new ReadOnlySpan<uint>(_props, (int)CountProps);

    /// <summary>
    /// Gets a span over the property values.
    /// </summary>
    public ReadOnlySpan<ulong> PropValues => _propValues == null
        ? ReadOnlySpan<ulong>.Empty
        : new ReadOnlySpan<ulong>(_propValues, (int)CountProps);
}