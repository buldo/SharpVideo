using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.Gbm;

namespace SharpVideo.Utils.Buffers;

/// <summary>
/// Provides factory methods and utilities for GBM (Generic Buffer Management) surfaces
/// used with EGL/OpenGL ES rendering.
/// </summary>
/// <remarks>
/// GBM surfaces are used as the native window type for EGL when rendering directly
/// to DRM/KMS without a display server. This helper simplifies the creation and
/// management of GBM surfaces for common use cases:
/// - SCANOUT + RENDERING: For EGL/OpenGL ES rendering to displayable buffers
/// - SCANOUT + LINEAR: For CPU-accessible scanout buffers
/// - SCANOUT only: For hardware-only scanout without CPU access
/// </remarks>
[SupportedOSPlatform("linux")]
public static class GbmSurfaceHelper
{
    /// <summary>
    /// Creates a GBM surface suitable for EGL/OpenGL ES rendering with display output.
    /// Uses SCANOUT + RENDERING flags for GPU-rendered content.
    /// </summary>
    /// <param name="gbmDevice">The GBM device.</param>
    /// <param name="width">Surface width in pixels.</param>
    /// <param name="height">Surface height in pixels.</param>
    /// <param name="pixelFormat">Pixel format (typically ARGB8888 for OSD with transparency).</param>
    /// <param name="logger">Optional logger.</param>
    /// <returns>A GBM surface configured for rendering.</returns>
    /// <exception cref="InvalidOperationException">If surface creation fails.</exception>
    public static GbmSurface CreateForRendering(
        GbmDevice gbmDevice,
        uint width,
        uint height,
        PixelFormat pixelFormat,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(gbmDevice);

        logger?.LogDebug(
            "Creating GBM surface for rendering: {Width}x{Height}, format={Format}",
            width, height, pixelFormat.GetName());

        var surface = gbmDevice.CreateSurface(
            width,
            height,
            pixelFormat,
            GbmBoUse.GBM_BO_USE_SCANOUT | GbmBoUse.GBM_BO_USE_RENDERING);

        logger?.LogInformation(
            "Created GBM surface {Width}x{Height} format={Format} for EGL rendering",
            width, height, pixelFormat.GetName());

        return surface;
    }

    /// <summary>
    /// Creates a GBM buffer object suitable for scanout with CPU access.
    /// Attempts SCANOUT + LINEAR first, falls back to SCANOUT only.
    /// </summary>
    /// <param name="gbmDevice">The GBM device handle.</param>
    /// <param name="width">Buffer width in pixels.</param>
    /// <param name="height">Buffer height in pixels.</param>
    /// <param name="pixelFormat">Pixel format.</param>
    /// <param name="logger">Optional logger.</param>
    /// <returns>A GBM buffer object handle (nint), or 0 on failure.</returns>
    public static nint CreateBoForScanout(
        nint gbmDevice,
        uint width,
        uint height,
        PixelFormat pixelFormat,
        ILogger? logger = null)
    {
        if (gbmDevice == 0)
        {
            throw new ArgumentException("Invalid GBM device handle", nameof(gbmDevice));
        }

        // Try with LINEAR for CPU mapping support
        var bo = LibGbm.CreateBo(
            gbmDevice,
            width,
            height,
            pixelFormat.Fourcc,
            GbmBoUse.GBM_BO_USE_SCANOUT | GbmBoUse.GBM_BO_USE_LINEAR);

        if (bo == 0)
        {
            // Fallback: some drivers don't support LINEAR
            logger?.LogDebug(
                "SCANOUT + LINEAR not supported, trying SCANOUT only");

            bo = LibGbm.CreateBo(
                gbmDevice,
                width,
                height,
                pixelFormat.Fourcc,
                GbmBoUse.GBM_BO_USE_SCANOUT);
        }

        if (bo != 0)
        {
            logger?.LogDebug(
                "Created GBM BO {Width}x{Height} format={Format:X8}",
                width, height, pixelFormat.Fourcc);
        }
        else
        {
            logger?.LogError(
                "Failed to create GBM BO {Width}x{Height} format={Format:X8}",
                width, height, pixelFormat.Fourcc);
        }

        return bo;
    }

    /// <summary>
    /// Gets an EGL display from a GBM device for use with eglGetPlatformDisplay.
    /// </summary>
    /// <param name="gbmDevice">The GBM device.</param>
    /// <returns>The GBM device handle suitable for EGL_PLATFORM_GBM_KHR.</returns>
    public static nint GetEglDisplay(GbmDevice gbmDevice)
    {
        ArgumentNullException.ThrowIfNull(gbmDevice);
        return gbmDevice.Fd;
    }

    /// <summary>
    /// Locks the front buffer from a GBM surface after eglSwapBuffers.
    /// The returned buffer object must be released when no longer needed.
    /// </summary>
    /// <param name="surface">The GBM surface.</param>
    /// <param name="logger">Optional logger.</param>
    /// <returns>The locked buffer object handle, or 0 on failure.</returns>
    public static nint LockFrontBuffer(GbmSurface surface, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var bo = LibGbm.LockFrontBuffer(surface.Handle);
        if (bo == 0)
        {
            logger?.LogError("Failed to lock front buffer from GBM surface");
        }

        return bo;
    }

    /// <summary>
    /// Releases a buffer object back to the GBM surface.
    /// Call this after the buffer is no longer being displayed.
    /// </summary>
    /// <param name="surface">The GBM surface.</param>
    /// <param name="bo">The buffer object to release.</param>
    public static void ReleaseBuffer(GbmSurface surface, nint bo)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (bo != 0)
        {
            LibGbm.ReleaseBuffer(surface.Handle, bo);
        }
    }

    /// <summary>
    /// Maps a GBM buffer object for CPU access.
    /// </summary>
    /// <param name="bo">The buffer object handle.</param>
    /// <param name="width">Width of the region to map.</param>
    /// <param name="height">Height of the region to map.</param>
    /// <param name="stride">Output: stride of the mapped buffer.</param>
    /// <param name="mapData">Output: opaque map data for unmapping.</param>
    /// <param name="writeOnly">True for write-only access, false for read-write.</param>
    /// <returns>Pointer to the mapped memory, or null on failure.</returns>
    public static unsafe void* MapBo(
        nint bo,
        uint width,
        uint height,
        out uint stride,
        out void* mapData,
        bool writeOnly = true)
    {
        stride = 0;
        mapData = null;

        if (bo == 0)
        {
            return null;
        }

        uint strideOut;
        void* mapDataOut = null;

        var flags = writeOnly
            ? LibGbm.GbmBoTransferFlags.GBM_BO_TRANSFER_WRITE
            : LibGbm.GbmBoTransferFlags.GBM_BO_TRANSFER_READ_WRITE;

        var ptr = LibGbm.Map(bo, 0, 0, width, height, flags, &strideOut, &mapDataOut);

        stride = strideOut;
        mapData = mapDataOut;

        return ptr;
    }

    /// <summary>
    /// Unmaps a previously mapped GBM buffer object.
    /// </summary>
    /// <param name="bo">The buffer object handle.</param>
    /// <param name="mapData">The opaque map data returned from MapBo.</param>
    public static unsafe void UnmapBo(nint bo, void* mapData)
    {
        if (bo != 0 && mapData != null)
        {
            LibGbm.Unmap(bo, mapData);
        }
    }

    /// <summary>
    /// Gets the stride (bytes per row) of a GBM buffer object.
    /// </summary>
    /// <param name="bo">The buffer object handle.</param>
    /// <returns>The stride in bytes.</returns>
    public static uint GetStride(nint bo)
    {
        return LibGbm.GetStride(bo);
    }

    /// <summary>
    /// Gets the GEM handle for a GBM buffer object.
    /// </summary>
    /// <param name="bo">The buffer object handle.</param>
    /// <returns>The GEM handle.</returns>
    public static uint GetHandle(nint bo)
    {
        return LibGbm.GetHandle(bo);
    }

    /// <summary>
    /// Gets a DMA-BUF file descriptor for a GBM buffer object.
    /// The caller is responsible for closing the returned fd.
    /// </summary>
    /// <param name="bo">The buffer object handle.</param>
    /// <returns>The DMA-BUF fd, or -1 on failure.</returns>
    public static int GetFd(nint bo)
    {
        return LibGbm.GetFd(bo);
    }

    /// <summary>
    /// Destroys a GBM buffer object.
    /// </summary>
    /// <param name="bo">The buffer object handle.</param>
    public static void DestroyBo(nint bo)
    {
        if (bo != 0)
        {
            LibGbm.DestroyBo(bo);
        }
    }
}
