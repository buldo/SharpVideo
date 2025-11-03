using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SharpVideo.Linux.Native.Gbm;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Native GBM (Generic Buffer Manager) bindings for EGL platform
/// </summary>
[SupportedOSPlatform("linux")]
public static unsafe class LibGbm
{
    private const string LibraryName = "libgbm.so.1";

    // Device functions
    [DllImport(LibraryName, EntryPoint = "gbm_create_device")]
    public static extern nint CreateDevice(int fd);

    [DllImport(LibraryName, EntryPoint = "gbm_device_destroy")]
    public static extern void DestroyDevice(nint gbm);

    [DllImport(LibraryName, EntryPoint = "gbm_device_get_fd")]
    public static extern int DeviceGetFd(nint gbm);

    // GBM Surface functions
    [DllImport(LibraryName, EntryPoint = "gbm_surface_create")]
    public static extern nint CreateSurface(nint gbm, uint width, uint height, uint format, GbmBoUse flags);

    [DllImport(LibraryName, EntryPoint = "gbm_surface_destroy")]
    public static extern void DestroySurface(nint surface);

    /// <summary>
    /// Lock the surface's current front buffer for rendering.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "gbm_surface_lock_front_buffer")]
    public static extern nint LockFrontBuffer(nint surface);

    /// <summary>
    /// Release a buffer object back to the surface.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "gbm_surface_release_buffer")]
    public static extern void ReleaseBuffer(nint surface, nint bo);

    // GBM Buffer Object (BO) property getters
    /// <summary>
    /// Get the width of a buffer object.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "gbm_bo_get_width")]
    public static extern uint GetWidth(nint bo);

    /// <summary>
    /// Get the height of a buffer object.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "gbm_bo_get_height")]
    public static extern uint GetHeight(nint bo);

    /// <summary>
    /// Get the stride (pitch) of a buffer object in bytes.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "gbm_bo_get_stride")]
    public static extern uint GetStride(nint bo);

    /// <summary>
    /// Get the format of a buffer object.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "gbm_bo_get_format")]
    public static extern uint GetFormat(nint bo);

    /// <summary>
    /// Get the handle of a buffer object (for use with DRM).
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "gbm_bo_get_handle")]
    public static extern nint GetHandleNative(nint bo);

    /// <summary>
    /// Get the handle of a buffer object as a uint32.
    /// </summary>
    public static uint GetHandle(nint bo)
    {
        var handleUnion = GetHandleNative(bo);
        // The handle union's u32 field is the first field, so we can cast directly
        return (uint)(long)handleUnion;
    }

    /// <summary>
    /// Get the file descriptor of a buffer object.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "gbm_bo_get_fd")]
    public static extern int GetFd(nint bo);
}