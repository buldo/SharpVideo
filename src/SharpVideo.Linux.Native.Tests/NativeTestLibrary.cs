using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native.Tests;

/// <summary>
/// P/Invoke declarations for the native test library
/// </summary>
public static unsafe partial class NativeTestLibrary
{
    private const string LibraryName = "libtest_structures.so";

    // Function to fill drmModeRes structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_drm_mode_res")]
    public static partial void FillNativeDrmModeRes(DrmModeRes* structure);

    // Function to get drmModeRes structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_drm_mode_res_size")]
    public static partial int GetNativeDrmModeResSize();

    // Function to fill drmModeEncoder structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_drm_mode_encoder")]
    public static partial void FillNativeDrmModeEncoder(DrmModeEncoder* structure);

    // Function to get drmModeEncoder structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_drm_mode_encoder_size")]
    public static partial int GetNativeDrmModeEncoderSize();

    // Function to fill drmModeConnector structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_drm_mode_connector")]
    public static partial void FillNativeDrmModeConnector(DrmModeConnector* structure);

    // Function to get drmModeConnector structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_drm_mode_connector_size")]
    public static partial int GetNativeDrmModeConnectorSize();

    // Function to fill drmModeCrtc structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_drm_mode_crtc")]
    public static partial void FillNativeDrmModeCrtc(DrmModeCrtc* structure);

    // Function to get drmModeCrtc structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_drm_mode_crtc_size")]
    public static partial int GetNativeDrmModeCrtcSize();

    // Function to fill drmModeModeInfo structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_drm_mode_mode_info")]
    public static partial void FillNativeDrmModeModeInfo(DrmModeModeInfo* structure);

    // Function to get drmModeModeInfo structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_drm_mode_mode_info_size")]
    public static partial int GetNativeDrmModeModeInfoSize();

    // Function to fill drmModePlane structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_drm_mode_plane")]
    public static partial void FillNativeDrmModePlane(DrmModePlane* structure);

    // Function to get drmModePlane structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_drm_mode_plane_size")]
    public static partial int GetNativeDrmModePlaneSize();

    // Function to fill drmModeFB structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_drm_mode_fb")]
    public static partial void FillNativeDrmModeFB(DrmModeFB* structure);

    // Function to get drmModeFB structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_drm_mode_fb_size")]
    public static partial int GetNativeDrmModeFBSize();

    // Function to fill dma_heap_allocation_data structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_dma_heap_allocation_data")]
    public static partial void FillNativeDmaHeapAllocationData(DmaHeapAllocationData* structure);

    // Function to get dma_heap_allocation_data structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_dma_heap_allocation_data_size")]
    public static partial int GetNativeDmaHeapAllocationDataSize();

    // Function to get the real DMA_HEAP_IOCTL_ALLOC constant value from Linux headers
    [LibraryImport(LibraryName, EntryPoint = "get_native_dma_heap_ioctl_alloc")]
    public static partial uint GetNativeDmaHeapIoctlAlloc();
}