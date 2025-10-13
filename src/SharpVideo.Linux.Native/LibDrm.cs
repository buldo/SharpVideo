using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

[SupportedOSPlatform("linux")]
public static unsafe partial class LibDrm
{
    private const string LibraryName = "drm";

    // -------------------- Constants ------------------------------

    /// <summary>DRM mode object type: CRTC</summary>
    public const uint DRM_MODE_OBJECT_CRTC = 0xcccccccc;

    /// <summary>DRM mode object type: Connector</summary>
    public const uint DRM_MODE_OBJECT_CONNECTOR = 0xc0c0c0c0;

    /// <summary>DRM mode object type: Encoder</summary>
    public const uint DRM_MODE_OBJECT_ENCODER = 0xe0e0e0e0;

    /// <summary>DRM mode object type: Mode</summary>
    public const uint DRM_MODE_OBJECT_MODE = 0xdededede;

    /// <summary>DRM mode object type: Property</summary>
    public const uint DRM_MODE_OBJECT_PROPERTY = 0xb0b0b0b0;

    /// <summary>DRM mode object type: Framebuffer</summary>
    public const uint DRM_MODE_OBJECT_FB = 0xfbfbfbfb;

    /// <summary>DRM mode object type: Blob</summary>
    public const uint DRM_MODE_OBJECT_BLOB = 0xbbbbbbbb;

    /// <summary>DRM mode object type: Plane</summary>
    public const uint DRM_MODE_OBJECT_PLANE = 0xeeeeeeee;

    // -------------------- P/Invoke ------------------------------

    /// <summary>
    /// Set a capability for the DRM client.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="capability">The capability to set</param>
    /// <param name="value">The value to set (1 to enable, 0 to disable)</param>
    /// <returns>0 on success, negative error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmSetClientCap")]
    public static partial int drmSetClientCap(int fd, DrmClientCapability capability, UInt64 value);

    /// <summary>
    /// Query a capability of the DRM device.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="capability">The capability to query</param>
    /// <param name="value">Returned capability value</param>
    /// <returns>0 on success, negative error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmGetCap")]
    public static partial nint drmGetCap(int fd, DrmCapability capability, out UInt64 value);

    /// <summary>
    /// Retrieve resource handles for a given DRM file descriptor.
    /// The returned pointer must be freed with <see cref="drmModeFreeResources" />.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <returns>Pointer to a <see cref="DrmModeRes"/> structure, or <c>IntPtr.Zero</c> on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeGetResources")]
    public static partial DrmModeRes* drmModeGetResources(int fd);

    /// <summary>
    /// Free a structure obtained from <see cref="drmModeGetResources" />.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "drmModeFreeResources")]
    public static partial void drmModeFreeResources(DrmModeRes* resources);

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
    public static partial void drmModeFreePlane(DrmModePlane* plane);    /// <summary>
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
    public static partial DrmModeObjectProperties* drmModeObjectGetProperties(
        int fd,
        uint object_id,
        uint object_type);

    /// <summary>
    /// Free a structure obtained from <see cref="drmModeObjectGetProperties" />.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "drmModeFreeObjectProperties")]
    public static partial void drmModeFreeObjectProperties(DrmModeObjectProperties* properties);

    /// <summary>
    /// Add a framebuffer to the DRM device using a DMA buffer file descriptor.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="width">Framebuffer width in pixels</param>
    /// <param name="height">Framebuffer height in pixels</param>
    /// <param name="depth">Color depth</param>
    /// <param name="bpp">Bits per pixel</param>
    /// <param name="pitch">Bytes per row</param>
    /// <param name="bo_handle">Buffer object handle</param>
    /// <param name="buf_id">Returned framebuffer ID</param>
    /// <returns>0 on success, negative error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeAddFB")]
    public static partial int drmModeAddFB(
        int fd,
        uint width,
        uint height,
        byte depth,
        byte bpp,
        uint pitch,
        uint bo_handle,
        out uint buf_id);

    /// <summary>
    /// Add a framebuffer to the DRM device using multiple DMA buffer file descriptors (version 2).
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="width">Framebuffer width in pixels</param>
    /// <param name="height">Framebuffer height in pixels</param>
    /// <param name="pixel_format">Pixel format identifier</param>
    /// <param name="bo_handles">Array of buffer object handles</param>
    /// <param name="pitches">Array of pitches (bytes per row)</param>
    /// <param name="offsets">Array of offsets for each plane</param>
    /// <param name="buf_id">Returned framebuffer ID</param>
    /// <param name="flags">Framebuffer configuration flags</param>
    /// <returns>0 on success, negative error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeAddFB2")]
    public static partial int drmModeAddFB2(
        int fd,
        uint width,
        uint height,
        uint pixel_format,
        uint* bo_handles,
        uint* pitches,
        uint* offsets,
        out uint buf_id,
        uint flags);

    /// <summary>
    /// Set the CRTC configuration.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="crtc_id">CRTC ID</param>
    /// <param name="buffer_id">Framebuffer ID to display</param>
    /// <param name="x">X offset in framebuffer</param>
    /// <param name="y">Y offset in framebuffer</param>
    /// <param name="connectors">Array of connector IDs</param>
    /// <param name="count">Number of connectors</param>
    /// <param name="mode">Display mode to set</param>
    /// <returns>0 on success, negative error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeSetCrtc")]
    public static partial int drmModeSetCrtc(
        int fd,
        uint crtc_id,
        uint buffer_id,
        uint x,
        uint y,
        uint* connectors,
        int count,
        DrmModeModeInfo* mode);

    /// <summary>
    /// Remove a framebuffer from the DRM device.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="buffer_id">Framebuffer ID to remove</param>
    /// <returns>0 on success, negative error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeRmFB")]
    public static partial int drmModeRmFB(int fd, uint buffer_id);

    /// <summary>
    /// Convert a DMA buffer file descriptor to a DRM buffer object handle.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="prime_fd">DMA buffer file descriptor</param>
    /// <param name="handle">Returned buffer object handle</param>
    /// <returns>0 on success, negative error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmPrimeFDToHandle")]
    public static partial int drmPrimeFDToHandle(int fd, int prime_fd, out uint handle);

    /// <summary>
    /// Configure a plane to display a framebuffer.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="plane_id">Plane ID</param>
    /// <param name="crtc_id">CRTC ID to associate with the plane</param>
    /// <param name="fb_id">Framebuffer ID to display</param>
    /// <param name="flags">Plane configuration flags</param>
    /// <param name="crtc_x">X position on CRTC</param>
    /// <param name="crtc_y">Y position on CRTC</param>
    /// <param name="crtc_w">Width on CRTC</param>
    /// <param name="crtc_h">Height on CRTC</param>
    /// <param name="src_x">X position in source framebuffer (in 16.16 fixed point)</param>
    /// <param name="src_y">Y position in source framebuffer (in 16.16 fixed point)</param>
    /// <param name="src_w">Width in source framebuffer (in 16.16 fixed point)</param>
    /// <param name="src_h">Height in source framebuffer (in 16.16 fixed point)</param>
    /// <returns>0 on success, negative error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeSetPlane")]
    public static partial int drmModeSetPlane(
        int fd,
        uint plane_id,
        uint crtc_id,
        uint fb_id,
        uint flags,
        int crtc_x, int crtc_y,
        uint crtc_w, uint crtc_h,
        uint src_x, uint src_y,
        uint src_w, uint src_h);

    [LibraryImport(LibraryName, EntryPoint = "drmModeObjectSetProperty")]
    public static partial int drmModeObjectSetProperty(
        int fd,
        uint object_id,
        uint object_type,
        uint property_id,
        ulong property_value);

    // -------------------- Atomic Modesetting API ------------------------------

    /// <summary>
    /// Allocate a new atomic request object.
    /// Must be freed with drmModeAtomicFree.
    /// </summary>
    /// <returns>Pointer to atomic request, or null on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeAtomicAlloc")]
    public static partial DrmModeAtomicReq* drmModeAtomicAlloc();

    /// <summary>
    /// Free an atomic request object.
    /// </summary>
    /// <param name="req">Atomic request to free</param>
    [LibraryImport(LibraryName, EntryPoint = "drmModeAtomicFree")]
    public static partial void drmModeAtomicFree(DrmModeAtomicReq* req);

    /// <summary>
    /// Add a property to an atomic request.
    /// </summary>
    /// <param name="req">Atomic request</param>
    /// <param name="object_id">Object ID (CRTC, plane, connector, etc.)</param>
    /// <param name="property_id">Property ID</param>
    /// <param name="value">Property value</param>
    /// <returns>0 on success, negative on error</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeAtomicAddProperty")]
    public static partial int drmModeAtomicAddProperty(
        DrmModeAtomicReq* req,
        uint object_id,
        uint property_id,
        ulong value);

    /// <summary>
    /// Commit an atomic request.
    /// </summary>
    /// <param name="fd">Open DRM device file descriptor</param>
    /// <param name="req">Atomic request to commit</param>
    /// <param name="flags">Commit flags</param>
    /// <param name="user_data">User data for page flip event</param>
    /// <returns>0 on success, negative on error</returns>
    [LibraryImport(LibraryName, EntryPoint = "drmModeAtomicCommit")]
    public static partial int drmModeAtomicCommit(
        int fd,
        DrmModeAtomicReq* req,
        DrmModeAtomicFlags flags,
        nint user_data);
}
