using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drmModeFormatModifierIterator</c> structure.
/// Used for iterating over format modifiers.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct DrmModeFormatModifierIterator
{
    /// <summary>
    /// Format index.
    /// </summary>
    public readonly uint FmtIdx;

    /// <summary>
    /// Modifier index.
    /// </summary>
    public readonly uint ModIdx;

    /// <summary>
    /// Format value.
    /// </summary>
    public readonly uint Fmt;

    /// <summary>
    /// Modifier value.
    /// </summary>
    public readonly ulong Mod;
}