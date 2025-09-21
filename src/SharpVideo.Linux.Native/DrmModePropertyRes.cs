using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drmModePropertyRes</c> structure.
/// Contains DRM mode property information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DrmModePropertyRes
{
    /// <summary>
    /// Property ID.
    /// </summary>
    public readonly uint PropId;

    /// <summary>
    /// Property flags.
    /// </summary>
    public readonly uint Flags;

    /// <summary>
    /// Property name (32 characters).
    /// </summary>
    public fixed byte Name[32];

    /// <summary>
    /// Number of values.
    /// </summary>
    public readonly int CountValues;

    /// <summary>
    /// Pointer to property values array.
    /// </summary>
    private readonly ulong* _values;

    /// <summary>
    /// Number of enums.
    /// </summary>
    public readonly int CountEnums;

    /// <summary>
    /// Pointer to enum values array.
    /// </summary>
    private readonly void* _enums; // drm_mode_property_enum*

    /// <summary>
    /// Number of blob IDs.
    /// </summary>
    public readonly int CountBlobs;

    /// <summary>
    /// Pointer to blob IDs array.
    /// </summary>
    private readonly uint* _blobIds;

    /// <summary>
    /// Gets the property name as a string.
    /// </summary>
    public string NameString
    {
        get
        {
            fixed (byte* namePtr = Name)
            {
                // Find length up to first null terminator
                int len = 0;
                while (len < 32 && namePtr[len] != 0) len++;
                return len == 0 ? string.Empty : System.Text.Encoding.UTF8.GetString(namePtr, len);
            }
        }
    }

    /// <summary>
    /// Gets a span over the property values.
    /// </summary>
    public ReadOnlySpan<ulong> Values => _values == null
        ? ReadOnlySpan<ulong>.Empty
        : new ReadOnlySpan<ulong>(_values, CountValues);

    /// <summary>
    /// Gets a span over the blob IDs.
    /// </summary>
    public ReadOnlySpan<uint> BlobIds => _blobIds == null
        ? ReadOnlySpan<uint>.Empty
        : new ReadOnlySpan<uint>(_blobIds, CountBlobs);

    public PropertyType Type => (PropertyType)(Flags & ((uint)PropertyType.DRM_MODE_PROP_LEGACY_TYPE | (uint)PropertyType.DRM_MODE_PROP_EXTENDED_TYPE));
}