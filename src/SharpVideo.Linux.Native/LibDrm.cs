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

    /// <summary>
    /// Retrieve connector information for a given connector ID.
    /// The returned pointer must be freed with <see cref="drmModeFreeConnector" />.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="connector_id">Connector ID to query</param>
    /// <returns>Pointer to a <see cref="DrmModeConnector"/> structure, or <c>null</c> on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeGetConnector")]
    public static partial DrmModeConnector* drmModeGetConnector(int fd, uint connector_id);

    /// <summary>
    /// Free a structure obtained from <see cref="drmModeGetConnector" />.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "drmModeFreeConnector")]
    public static partial void drmModeFreeConnector(DrmModeConnector* connector);

    /// <summary>
    /// Retrieve CRTC information for a given CRTC ID.
    /// The returned pointer must be freed with <see cref="drmModeFreeCrtc" />.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="crtc_id">CRTC ID to query</param>
    /// <returns>Pointer to a <see cref="DrmModeCrtc"/> structure, or <c>null</c> on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeGetCrtc")]
    public static partial DrmModeCrtc* drmModeGetCrtc(int fd, uint crtc_id);

    /// <summary>
    /// Free a structure obtained from <see cref="drmModeGetCrtc" />.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "drmModeFreeCrtc")]
    public static partial void drmModeFreeCrtc(DrmModeCrtc* crtc);

    /// <summary>
    /// Retrieve plane resources for a given DRM file descriptor.
    /// The returned pointer must be freed with <see cref="drmModeFreePlaneResources" />.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <returns>Pointer to a <see cref="DrmModePlaneRes"/> structure, or <c>null</c> on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeGetPlaneResources")]
    public static partial DrmModePlaneRes* drmModeGetPlaneResources(int fd);

    /// <summary>
    /// Free a structure obtained from <see cref="drmModeGetPlaneResources" />.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "drmModeFreePlaneResources")]
    public static partial void drmModeFreePlaneResources(DrmModePlaneRes* resources);

    /// <summary>
    /// Retrieve plane information for a given plane ID.
    /// The returned pointer must be freed with <see cref="drmModeFreePlane" />.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="plane_id">Plane ID to query</param>
    /// <returns>Pointer to a <see cref="DrmModePlane"/> structure, or <c>null</c> on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeGetPlane")]
    public static partial DrmModePlane* drmModeGetPlane(int fd, uint plane_id);

    /// <summary>
    /// Free a structure obtained from <see cref="drmModeGetPlane" />.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "drmModeFreePlane")]
    public static partial void drmModeFreePlane(DrmModePlane* plane);

    /// <summary>
    /// Retrieve framebuffer information for a given framebuffer ID.
    /// The returned pointer must be freed with <see cref="drmModeFreeFB" />.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="fb_id">Framebuffer ID to query</param>
    /// <returns>Pointer to a <see cref="DrmModeFB"/> structure, or <c>null</c> on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeGetFB")]
    public static partial DrmModeFB* drmModeGetFB(int fd, uint fb_id);

    /// <summary>
    /// Free a structure obtained from <see cref="drmModeGetFB" />.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "drmModeFreeFB")]
    public static partial void drmModeFreeFB(DrmModeFB* fb);

    /// <summary>
    /// Retrieve framebuffer information for a given framebuffer ID (version 2).
    /// The returned pointer must be freed with <see cref="drmModeFreeFB2" />.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="fb_id">Framebuffer ID to query</param>
    /// <returns>Pointer to a <see cref="DrmModeFB2"/> structure, or <c>null</c> on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeGetFB2")]
    public static partial DrmModeFB2* drmModeGetFB2(int fd, uint fb_id);

    /// <summary>
    /// Free a structure obtained from <see cref="drmModeGetFB2" />.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "drmModeFreeFB2")]
    public static partial void drmModeFreeFB2(DrmModeFB2* fb);

    /// <summary>
    /// Retrieve property information for a given property ID.
    /// The returned pointer must be freed with <see cref="drmModeFreeProperty" />.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="property_id">Property ID to query</param>
    /// <returns>Pointer to a <see cref="DrmModePropertyRes"/> structure, or <c>null</c> on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeGetProperty")]
    public static partial DrmModePropertyRes* drmModeGetProperty(int fd, uint property_id);

    /// <summary>
    /// Free a structure obtained from <see cref="drmModeGetProperty" />.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "drmModeFreeProperty")]
    public static partial void drmModeFreeProperty(DrmModePropertyRes* property);

    /// <summary>
    /// Retrieve property blob information for a given blob ID.
    /// The returned pointer must be freed with <see cref="drmModeFreePropertyBlob" />.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="blob_id">Property blob ID to query</param>
    /// <returns>Pointer to a <see cref="DrmModePropertyBlobRes"/> structure, or <c>null</c> on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeGetPropertyBlob")]
    public static partial DrmModePropertyBlobRes* drmModeGetPropertyBlob(int fd, uint blob_id);

    /// <summary>
    /// Free a structure obtained from <see cref="drmModeGetPropertyBlob" />.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "drmModeFreePropertyBlob")]
    public static partial void drmModeFreePropertyBlob(DrmModePropertyBlobRes* blob);

    /// <summary>
    /// Retrieve object properties for a given object ID and type.
    /// The returned pointer must be freed with <see cref="drmModeFreeObjectProperties" />.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="object_id">Object ID to query</param>
    /// <param name="object_type">Object type</param>
    /// <returns>Pointer to a <see cref="DrmModeObjectProperties"/> structure, or <c>null</c> on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeObjectGetProperties")]
    public static partial DrmModeObjectProperties* drmModeObjectGetProperties(int fd, uint object_id, uint object_type);

    /// <summary>
    /// Free a structure obtained from <see cref="drmModeObjectGetProperties" />.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "drmModeFreeObjectProperties")]
    public static partial void drmModeFreeObjectProperties(DrmModeObjectProperties* properties);

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

    /// <summary>
    /// Obtain a pointer to <see cref="DrmModeConnector"/> for the given connector ID.
    /// The caller is responsible for eventually invoking
    /// <see cref="FreeConnector(DrmModeConnector*)"/> when done.
    /// </summary>
    public static DrmModeConnector* GetConnector(int fd, uint connector_id) =>
        drmModeGetConnector(fd, connector_id);

    /// <summary>
    /// Free the connector structure obtained from <see cref="GetConnector"/>.
    /// </summary>
    public static void FreeConnector(DrmModeConnector* connector) =>
        drmModeFreeConnector(connector);

    /// <summary>
    /// Obtain a pointer to <see cref="DrmModeCrtc"/> for the given CRTC ID.
    /// The caller is responsible for eventually invoking
    /// <see cref="FreeCrtc(DrmModeCrtc*)"/> when done.
    /// </summary>
    public static DrmModeCrtc* GetCrtc(int fd, uint crtc_id) =>
        drmModeGetCrtc(fd, crtc_id);

    /// <summary>
    /// Free the CRTC structure obtained from <see cref="GetCrtc"/>.
    /// </summary>
    public static void FreeCrtc(DrmModeCrtc* crtc) =>
        drmModeFreeCrtc(crtc);

    /// <summary>
    /// Obtain a pointer to <see cref="DrmModePlaneRes"/> for the given DRM file descriptor.
    /// The caller is responsible for eventually invoking
    /// <see cref="FreePlaneResources(DrmModePlaneRes*)"/> when done.
    /// </summary>
    public static DrmModePlaneRes* GetPlaneResources(int fd) =>
        drmModeGetPlaneResources(fd);

    /// <summary>
    /// Free the plane resources structure obtained from <see cref="GetPlaneResources"/>.
    /// </summary>
    public static void FreePlaneResources(DrmModePlaneRes* resources) =>
        drmModeFreePlaneResources(resources);

    /// <summary>
    /// Obtain a pointer to <see cref="DrmModePlane"/> for the given plane ID.
    /// The caller is responsible for eventually invoking
    /// <see cref="FreePlane(DrmModePlane*)"/> when done.
    /// </summary>
    public static DrmModePlane* GetPlane(int fd, uint plane_id) =>
        drmModeGetPlane(fd, plane_id);

    /// <summary>
    /// Free the plane structure obtained from <see cref="GetPlane"/>.
    /// </summary>
    public static void FreePlane(DrmModePlane* plane) =>
        drmModeFreePlane(plane);

    /// <summary>
    /// Obtain a pointer to <see cref="DrmModeFB"/> for the given framebuffer ID.
    /// The caller is responsible for eventually invoking
    /// <see cref="FreeFB(DrmModeFB*)"/> when done.
    /// </summary>
    public static DrmModeFB* GetFB(int fd, uint fb_id) =>
        drmModeGetFB(fd, fb_id);

    /// <summary>
    /// Free the framebuffer structure obtained from <see cref="GetFB"/>.
    /// </summary>
    public static void FreeFB(DrmModeFB* fb) =>
        drmModeFreeFB(fb);

    /// <summary>
    /// Obtain a pointer to <see cref="DrmModeFB2"/> for the given framebuffer ID.
    /// The caller is responsible for eventually invoking
    /// <see cref="FreeFB2(DrmModeFB2*)"/> when done.
    /// </summary>
    public static DrmModeFB2* GetFB2(int fd, uint fb_id) =>
        drmModeGetFB2(fd, fb_id);

    /// <summary>
    /// Free the framebuffer structure obtained from <see cref="GetFB2"/>.
    /// </summary>
    public static void FreeFB2(DrmModeFB2* fb) =>
        drmModeFreeFB2(fb);

    /// <summary>
    /// Obtain a pointer to <see cref="DrmModePropertyRes"/> for the given property ID.
    /// The caller is responsible for eventually invoking
    /// <see cref="FreeProperty(DrmModePropertyRes*)"/> when done.
    /// </summary>
    public static DrmModePropertyRes* GetProperty(int fd, uint property_id) =>
        drmModeGetProperty(fd, property_id);

    /// <summary>
    /// Free the property structure obtained from <see cref="GetProperty"/>.
    /// </summary>
    public static void FreeProperty(DrmModePropertyRes* property) =>
        drmModeFreeProperty(property);

    /// <summary>
    /// Obtain a pointer to <see cref="DrmModePropertyBlobRes"/> for the given blob ID.
    /// The caller is responsible for eventually invoking
    /// <see cref="FreePropertyBlob(DrmModePropertyBlobRes*)"/> when done.
    /// </summary>
    public static DrmModePropertyBlobRes* GetPropertyBlob(int fd, uint blob_id) =>
        drmModeGetPropertyBlob(fd, blob_id);

    /// <summary>
    /// Free the property blob structure obtained from <see cref="GetPropertyBlob"/>.
    /// </summary>
    public static void FreePropertyBlob(DrmModePropertyBlobRes* blob) =>
        drmModeFreePropertyBlob(blob);

    /// <summary>
    /// Obtain a pointer to <see cref="DrmModeObjectProperties"/> for the given object ID and type.
    /// The caller is responsible for eventually invoking
    /// <see cref="FreeObjectProperties(DrmModeObjectProperties*)"/> when done.
    /// </summary>
    public static DrmModeObjectProperties* GetObjectProperties(int fd, uint object_id, uint object_type) =>
        drmModeObjectGetProperties(fd, object_id, object_type);

    /// <summary>
    /// Free the object properties structure obtained from <see cref="GetObjectProperties"/>.
    /// </summary>
    public static void FreeObjectProperties(DrmModeObjectProperties* properties) =>
        drmModeFreeObjectProperties(properties);
}
