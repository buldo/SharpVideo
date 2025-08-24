using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

[SupportedOSPlatform("linux")]
public static unsafe partial class LibDrm
{
    // ------------------------------------------------------------
    //  libdrm ‑ drmModeGetResources
    // ------------------------------------------------------------

    // libdrm is typically available as "libdrm.so.2" or through the
    // linker alias "drm".  Using "drm" keeps the binding portable.
    private const string LibraryName = "drm";

    // -------------------- P/Invoke ------------------------------

    /// <summary>
    /// Retrieve resource handles for a given DRM file descriptor.
    /// The returned pointer must be freed with <see cref="drmModeFreeResources" />.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <returns>Pointer to a <see cref="DrmModeRes"/> structure, or <c>IntPtr.Zero</c> on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeGetResources")]
    internal static partial nint drmModeGetResources(int fd);

    /// <summary>
    /// Free a structure obtained from <see cref="drmModeGetResources" />.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "drmModeFreeResources")]
    internal static partial void drmModeFreeResources(nint resources);

    /// <summary>
    /// Retrieve encoder information for a given encoder ID.
    /// The returned pointer must be freed with <see cref="drmModeFreeEncoder" />.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="encoder_id">Encoder ID to query</param>
    /// <returns>Pointer to a <see cref="DrmModeEncoder"/> structure, or <c>IntPtr.Zero</c> on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeGetEncoder")]
    public static partial DrmModeEncoder* drmModeGetEncoder(int fd, uint encoder_id);

    /// <summary>
    /// Free a structure obtained from <see cref="drmModeGetEncoder" />.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "drmModeFreeEncoder")]
    public static partial void drmModeFreeEncoder(DrmModeEncoder* encoder);

    // ------------------- Managed helpers ------------------------

    /// <summary>
    /// Obtain a pointer to <see cref="DrmModeRes"/> for the given DRM file descriptor.
    /// The caller is responsible for eventually invoking
    /// <see cref="FreeResources(DrmModeRes*)"/> when done.
    /// </summary>
    public static DrmModeRes* GetResources(int fd) =>
        (DrmModeRes*)drmModeGetResources(fd);

    /// <summary>
    /// Free the resources structure obtained from <see cref="GetResources"/>.
    /// </summary>
    public static void FreeResources(DrmModeRes* res) =>
        drmModeFreeResources((nint)res);
}
