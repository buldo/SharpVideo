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

    [DllImport(LibraryName, EntryPoint = "gbm_create_device")]
    public static extern nint CreateDevice(int fd);

    [DllImport(LibraryName, EntryPoint = "gbm_device_destroy")]
    public static extern void DestroyDevice(nint gbm);

    // GBM Surface functions
    [DllImport(LibraryName, EntryPoint = "gbm_surface_create")]
    public static extern nint CreateSurface(nint gbm, uint width, uint height, uint format, GbmBoUse flags);

    [DllImport(LibraryName, EntryPoint = "gbm_surface_destroy")]
    public static extern void DestroySurface(nint surface);

    [DllImport(LibraryName, EntryPoint = "gbm_device_get_fd")]
    public static extern int DeviceGetFd(nint gbm);
}