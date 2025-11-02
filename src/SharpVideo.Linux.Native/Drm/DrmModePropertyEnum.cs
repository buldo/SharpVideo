using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drm_mode_property_enum</c> structure.
/// Contains information about an enum value for a DRM property.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DrmModePropertyEnum
{
    /// <summary>
    /// Enum value.
    /// </summary>
    public readonly ulong Value;

    /// <summary>
    /// Enum name (32 characters).
    /// </summary>
    public fixed byte Name[32];

    /// <summary>
    /// Gets the enum name as a string.
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
}
