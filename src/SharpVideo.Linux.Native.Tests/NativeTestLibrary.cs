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
}