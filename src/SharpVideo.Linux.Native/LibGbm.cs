using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.MultiPlaneGlExample;

/// <summary>
/// Native GBM (Generic Buffer Manager) bindings for EGL platform
/// </summary>
[SupportedOSPlatform("linux")]
public static unsafe class LibGbm
{
    private const string LibraryName = "libgbm.so.1";

    // GBM buffer usage flags
    public const uint GBM_BO_USE_SCANOUT = 1 << 0;
    public const uint GBM_BO_USE_CURSOR = 1 << 1;
    public const uint GBM_BO_USE_RENDERING = 1 << 2;
    public const uint GBM_BO_USE_WRITE = 1 << 3;
    public const uint GBM_BO_USE_LINEAR = 1 << 4;

    [DllImport(LibraryName, EntryPoint = "gbm_create_device")]
    public static extern nint CreateDevice(int fd);

    [DllImport(LibraryName, EntryPoint = "gbm_device_destroy")]
    public static extern void DestroyDevice(nint gbm);

    // GBM Surface functions
    [DllImport(LibraryName, EntryPoint = "gbm_surface_create")]
    public static extern nint CreateSurface(nint gbm, uint width, uint height, uint format, uint flags);

    [DllImport(LibraryName, EntryPoint = "gbm_surface_destroy")]
    public static extern void DestroySurface(nint surface);

    // GBM formats (fourcc)
    public const uint GBM_FORMAT_ARGB8888 = 0x34325241; // 'AR24'
    public const uint GBM_FORMAT_XRGB8888 = 0x34325258; // 'XR24'

    [DllImport(LibraryName, EntryPoint = "gbm_device_get_fd")]
    public static extern int DeviceGetFd(nint gbm);
}
