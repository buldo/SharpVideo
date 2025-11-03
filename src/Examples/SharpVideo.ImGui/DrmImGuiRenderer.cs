using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Silk.NET.OpenGLES;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Linux.Native.Gbm;
using SharpVideo.Utils;

namespace SharpVideo.ImGui;

/// <summary>
/// Hardware-accelerated OpenGL ES renderer for ImGui using DRM/GBM with EGL and DMA-BUF.
/// Renders ImGui directly to DRM buffers for page flip presentation without vsync limitations.
/// </summary>
[SupportedOSPlatform("linux")]
public unsafe class DrmImGuiRenderer : IDisposable
{
    private readonly ILogger _logger;
    private readonly GL _gl;
    private readonly int _width;
    private readonly int _height;

    // GBM device and surface
    private readonly GbmDevice _gbmDevice;
    private readonly GbmSurface _gbmSurface;

    // EGL context
    private readonly nint _eglDisplay;
    private readonly nint _eglContext;
    private readonly nint _eglDummySurface;

    // EGL extension functions
    private readonly NativeEgl.EglCreateImageKHR? _eglCreateImageKHR;
    private readonly NativeEgl.EglDestroyImageKHR? _eglDestroyImageKHR;
    private readonly NativeEgl.GlEGLImageTargetRenderbufferStorageOES? _glEGLImageTargetRenderbufferStorageOES;

    // Per-buffer OpenGL resources
    private readonly Dictionary<int, DmaBufferGlResources> _dmaBufferResources = new();

    public DrmImGuiRenderer(
        DrmDevice drmDevice,
        int width,
        int height,
        ILogger logger)
    {
        _width = width;
        _height = height;
        _logger = logger;

        _logger.LogInformation("Initializing DRM ImGui renderer with EGL and OpenGL ES context...");

        _gbmDevice = GbmDevice.CreateFromDrmDevice(drmDevice);
        _logger.LogDebug("Created GBM device: 0x{Device:X}", _gbmDevice.Fd);

        // Create GBM surface for rendering
        _gbmSurface = _gbmDevice.CreateSurface(
            (uint)width,
            (uint)height,
            KnownPixelFormats.DRM_FORMAT_ARGB8888,
            GbmBoUse.GBM_BO_USE_SCANOUT | GbmBoUse.GBM_BO_USE_RENDERING);

        _logger?.LogDebug("Created GBM surface: 0x{Surface:X}", _gbmSurface.Fd);

        // Get EGL display using GBM platform
        _eglDisplay = GetEglDisplayFromGbm(_gbmDevice);

        if (_eglDisplay == 0 || _eglDisplay == NativeEgl.EGL_NO_DISPLAY)
        {
            _gbmDevice.Dispose();
            throw new Exception("Failed to get EGL display from GBM device");
        }

        _logger?.LogDebug("Successfully obtained EGL display from GBM: 0x{Display:X}", _eglDisplay);

        // Initialize EGL
        if (!NativeEgl.Initialize(_eglDisplay, out int major, out int minor))
        {
            var error = NativeEgl.GetError();
            var errorMsg = NativeEgl.GetErrorString(error);

            _logger?.LogError("eglInitialize failed!");
            _logger?.LogError("Error: {Error} (code: 0x{ErrorCode:X})", errorMsg, error);

            _gbmDevice.Dispose();
            throw new Exception($"Failed to initialize EGL: {errorMsg} (error code: 0x{error:X})");
        }

        _logger?.LogInformation("✓ EGL initialized: version {Major}.{Minor}", major, minor);

        // Log EGL information
        var eglVendorPtr = NativeEgl.QueryString(_eglDisplay, NativeEgl.EGL_VENDOR);
        var eglVersionPtr = NativeEgl.QueryString(_eglDisplay, NativeEgl.EGL_VERSION);
        var eglExtensionsPtr = NativeEgl.QueryString(_eglDisplay, NativeEgl.EGL_EXTENSIONS);

        if (eglVendorPtr != 0)
        {
            var eglVendor = Marshal.PtrToStringAnsi(eglVendorPtr);
            _logger?.LogDebug("EGL vendor: {Vendor}", eglVendor);
        }

        if (eglVersionPtr != 0)
        {
            var eglVersion = Marshal.PtrToStringAnsi(eglVersionPtr);
            _logger?.LogDebug("EGL version: {Version}", eglVersion);
        }

        if (eglExtensionsPtr != 0)
        {
            var eglExtensions = Marshal.PtrToStringAnsi(eglExtensionsPtr);
            _logger?.LogDebug("EGL extensions: {Extensions}", eglExtensions);
        }

        // Choose config
        int[] configAttribs =
        [
            NativeEgl.EGL_SURFACE_TYPE, NativeEgl.EGL_WINDOW_BIT,
            NativeEgl.EGL_RED_SIZE, 1,
            NativeEgl.EGL_GREEN_SIZE, 1,
            NativeEgl.EGL_BLUE_SIZE, 1,
            NativeEgl.EGL_ALPHA_SIZE, 0,
            NativeEgl.EGL_RENDERABLE_TYPE, NativeEgl.EGL_OPENGL_ES2_BIT,
            NativeEgl.EGL_SAMPLES, 0,
            NativeEgl.EGL_NONE
        ];

        var config =
            ChooseConfigMatchingVisual(_eglDisplay, configAttribs, KnownPixelFormats.DRM_FORMAT_XRGB8888.Fourcc);
        _logger?.LogInformation("EGL config chosen");

        // Bind OpenGL ES API
        if (!NativeEgl.BindAPI(NativeEgl.EGL_OPENGL_ES_API))
        {
            var error = NativeEgl.GetError();
            throw new Exception($"Failed to bind OpenGL ES API: {NativeEgl.GetErrorString(error)}");
        }

        // Create context (OpenGL ES 2.0)
        int[] contextAttribs =
        [
            NativeEgl.EGL_CONTEXT_CLIENT_VERSION, 2,
            NativeEgl.EGL_NONE
        ];

        fixed (int* contextAttribsPtr = contextAttribs)
        {
            _eglContext = NativeEgl.CreateContext(_eglDisplay, config, NativeEgl.EGL_NO_CONTEXT, contextAttribsPtr);
            if (_eglContext == 0)
            {
                var error = NativeEgl.GetError();
                throw new Exception($"Failed to create EGL context: {NativeEgl.GetErrorString(error)}");
            }
        }

        _logger?.LogInformation("EGL context created");

        // Create EGL window surface from GBM surface
        _eglDummySurface = NativeEgl.CreateWindowSurface(_eglDisplay, config, _gbmSurface.Fd, null);
        if (_eglDummySurface == 0 || _eglDummySurface == NativeEgl.EGL_NO_SURFACE)
        {
            var error = NativeEgl.GetError();
            throw new Exception($"Failed to create EGL window surface: {NativeEgl.GetErrorString(error)}");
        }

        _logger?.LogInformation("EGL window surface created from GBM surface");

        // Make context current
        if (!NativeEgl.MakeCurrent(_eglDisplay, _eglDummySurface, _eglDummySurface, _eglContext))
        {
            var error = NativeEgl.GetError();
            throw new Exception($"Failed to make context current: {NativeEgl.GetErrorString(error)}");
        }

        // Initialize OpenGL ES with Silk.NET
        _gl = GL.GetApi(NativeEgl.GetProcAddress);
        _logger?.LogInformation("OpenGL ES initialized via Silk.NET");

        // Log GL info
        var vendor = _gl.GetStringS(StringName.Vendor);
        var renderer = _gl.GetStringS(StringName.Renderer);
        var version = _gl.GetStringS(StringName.Version);
        var shadingLanguageVersion = _gl.GetStringS(StringName.ShadingLanguageVersion);

        _logger?.LogInformation("GL Vendor: {Vendor}", vendor);
        _logger?.LogInformation("GL Renderer: {Renderer}", renderer);
        _logger?.LogInformation("GL Version: {Version}", version);
        _logger?.LogInformation("GLSL Version: {ShadingLanguageVersion}", shadingLanguageVersion);

// Load EGL extension functions
        var createImagePtr = NativeEgl.GetProcAddress("eglCreateImageKHR");
        var destroyImagePtr = NativeEgl.GetProcAddress("eglDestroyImageKHR");
        var targetRenderbufferPtr = NativeEgl.GetProcAddress("glEGLImageTargetRenderbufferStorageOES");

        if (createImagePtr == 0 || destroyImagePtr == 0 || targetRenderbufferPtr == 0)
        {
            throw new Exception(
                "Required EGL/GL extensions not available (EGL_KHR_image_base, EGL_EXT_image_dma_buf_import, GL_OES_EGL_image)");
        }

        _eglCreateImageKHR = Marshal.GetDelegateForFunctionPointer<NativeEgl.EglCreateImageKHR>(createImagePtr);
        _eglDestroyImageKHR = Marshal.GetDelegateForFunctionPointer<NativeEgl.EglDestroyImageKHR>(destroyImagePtr);
        _glEGLImageTargetRenderbufferStorageOES =
            Marshal.GetDelegateForFunctionPointer<NativeEgl.GlEGLImageTargetRenderbufferStorageOES>(
                targetRenderbufferPtr);

        _logger?.LogInformation("EGL DMA-BUF extensions loaded successfully");
        _logger?.LogInformation("DRM ImGui renderer initialized successfully");
    }

    /// <summary>
    /// Chooses an EGL config matching the GBM visual format
    /// </summary>
    private nint ChooseConfigMatchingVisual(nint display, int[] attribs, uint visualId)
    {
        fixed (int* attribsPtr = attribs)
        {
            if (!NativeEgl.ChooseConfig(display, attribsPtr, null, 0, out int count) || count == 0)
            {
                var error = NativeEgl.GetError();
                throw new Exception($"No EGL configs available: {NativeEgl.GetErrorString(error)}");
            }

            var configs = new nint[count];
            fixed (nint* configsPtr = configs)
            {
                if (!NativeEgl.ChooseConfig(display, attribsPtr, configsPtr, count, out int matched) || matched == 0)
                {
                    var error = NativeEgl.GetError();
                    throw new Exception(
                        $"No EGL configs with appropriate attributes: {NativeEgl.GetErrorString(error)}");
                }

                // Try to find a config with matching NATIVE_VISUAL_ID
                for (int i = 0; i < matched; i++)
                {
                    if (NativeEgl.GetConfigAttrib(display, configs[i], NativeEgl.EGL_NATIVE_VISUAL_ID, out int id))
                    {
                        if ((uint)id == visualId)
                        {
                            _logger?.LogDebug("Found EGL config matching visual ID 0x{VisualId:X}", visualId);
                            return configs[i];
                        }
                    }
                }

                // If no exact match, just use the first config
                _logger?.LogDebug("No exact visual match found, using first config");
                return configs[0];
            }
        }
    }

    /// <summary>
    /// Gets EGL display from GBM device
    /// </summary>
    private nint GetEglDisplayFromGbm(GbmDevice gbmDevice)
    {
        // Query client extensions first (before display is created)
        var clientExtPtr = NativeEgl.QueryString(NativeEgl.EGL_NO_DISPLAY, NativeEgl.EGL_EXTENSIONS);
        string? clientExtensions = null;
        if (clientExtPtr != 0)
        {
            clientExtensions = Marshal.PtrToStringAnsi(clientExtPtr);
            _logger?.LogDebug("EGL client extensions: {Extensions}", clientExtensions);
        }

        // Try eglGetPlatformDisplayEXT if available (preferred method)
        if (clientExtensions?.Contains("EGL_EXT_platform_base") == true)
        {
            _logger?.LogDebug("EGL_EXT_platform_base is available, using eglGetPlatformDisplayEXT");

            var getPlatformDisplayPtr = NativeEgl.GetProcAddress("eglGetPlatformDisplayEXT");
            if (getPlatformDisplayPtr != 0)
            {
                var eglGetPlatformDisplayEXT =
                    Marshal.GetDelegateForFunctionPointer<NativeEgl.EglGetPlatformDisplayEXT>(getPlatformDisplayPtr);

                var display = eglGetPlatformDisplayEXT(NativeEgl.EGL_PLATFORM_GBM_KHR, gbmDevice.Fd, null);
                if (display != 0 && display != NativeEgl.EGL_NO_DISPLAY)
                {
                    _logger?.LogInformation("✓ Got EGL display using eglGetPlatformDisplayEXT with GBM platform");
                    return display;
                }
            }
        }

        // Fallback to eglGetDisplay with GBM device
        _logger?.LogDebug("Falling back to eglGetDisplay with GBM device");
        var fallbackDisplay = NativeEgl.GetDisplay(gbmDevice.Fd);
        if (fallbackDisplay != 0 && fallbackDisplay != NativeEgl.EGL_NO_DISPLAY)
        {
            _logger?.LogInformation("✓ Got EGL display using eglGetDisplay with GBM device");
            return fallbackDisplay;
        }

        _logger?.LogError("Failed to get EGL display from GBM device");
        return 0;
    }

    /// <summary>
    /// Renders ImGui draw data directly to the DMA buffer using OpenGL ES
    /// </summary>
    public void RenderToDmaBuffer(SharedDmaBuffer dmaBuffer, Hexa.NET.ImGui.ImDrawDataPtr drawData)
    {
        // Get or create GL resources for this DMA buffer
        if (!_dmaBufferResources.TryGetValue(dmaBuffer.DmaBuffer.Fd, out var resources))
        {
            resources = CreateDmaBufferGlResources(dmaBuffer);
            _dmaBufferResources[dmaBuffer.DmaBuffer.Fd] = resources;
            _logger?.LogDebug("Created GL resources for DMA buffer FD={Fd}", dmaBuffer.DmaBuffer.Fd);
        }

        // Bind the framebuffer that renders to this DMA buffer
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, resources.Framebuffer);
        _gl.Viewport(0, 0, (uint)_width, (uint)_height);

        // Clear with transparent black background
        _gl.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        // Enable blending for ImGui
        _gl.Enable(EnableCap.Blend);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.ScissorTest);

        // Render ImGui draw data using ImGui OpenGL3 backend
// The backend will handle all the actual rendering
        Hexa.NET.ImGui.Backends.OpenGL3.ImGuiImplOpenGL3.RenderDrawData(drawData);

        // Ensure rendering is complete
        _gl.Finish();
    }

    private DmaBufferGlResources CreateDmaBufferGlResources(SharedDmaBuffer dmaBuffer)
    {
        if (_eglCreateImageKHR == null || _glEGLImageTargetRenderbufferStorageOES == null)
        {
            throw new InvalidOperationException("EGL extensions not loaded");
        }

        // Create EGLImage from DMA-BUF
        int[] imageAttribs =
        [
            NativeEgl.EGL_WIDTH, _width,
            NativeEgl.EGL_HEIGHT, _height,
            NativeEgl.EGL_LINUX_DRM_FOURCC_EXT, (int)NativeEgl.DRM_FORMAT_ARGB8888,
            NativeEgl.EGL_DMA_BUF_PLANE0_FD_EXT, dmaBuffer.DmaBuffer.Fd,
            NativeEgl.EGL_DMA_BUF_PLANE0_OFFSET_EXT, 0,
            NativeEgl.EGL_DMA_BUF_PLANE0_PITCH_EXT, (int)dmaBuffer.Stride,
            NativeEgl.EGL_NONE
        ];

        nint eglImage;
        fixed (int* attribsPtr = imageAttribs)
        {
            eglImage = _eglCreateImageKHR(_eglDisplay, NativeEgl.EGL_NO_CONTEXT,
                NativeEgl.EGL_LINUX_DMA_BUF_EXT, nint.Zero, attribsPtr);
        }

        if (eglImage == NativeEgl.EGL_NO_IMAGE)
        {
            var error = NativeEgl.GetError();
            throw new Exception($"Failed to create EGLImage from DMA-BUF: {NativeEgl.GetErrorString(error)}");
        }

        _logger?.LogDebug("Created EGLImage from DMA-BUF FD={Fd}", dmaBuffer.DmaBuffer.Fd);

        // Create renderbuffer and bind EGLImage to it
        var renderbuffer = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderbuffer);
        _glEGLImageTargetRenderbufferStorageOES((uint)RenderbufferTarget.Renderbuffer, eglImage);

        var glError = _gl.GetError();
        if (glError != GLEnum.NoError)
        {
            throw new Exception($"Failed to bind EGLImage to renderbuffer: {glError}");
        }

        _logger?.LogDebug("Bound EGLImage to renderbuffer");

        // Create framebuffer and attach renderbuffer
        var framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            RenderbufferTarget.Renderbuffer, renderbuffer);

        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new Exception($"Framebuffer is not complete: {status}");
        }

        _logger?.LogDebug("Framebuffer created and verified for DMA-BUF rendering");

        return new DmaBufferGlResources
        {
            EglImage = eglImage,
            Renderbuffer = renderbuffer,
            Framebuffer = framebuffer
        };
    }

    public void Dispose()
    {
        _logger?.LogInformation("Disposing DRM ImGui renderer...");

        // Cleanup per-buffer resources
        foreach (var (fd, resources) in _dmaBufferResources)
        {
            _gl.DeleteFramebuffer(resources.Framebuffer);
            _gl.DeleteRenderbuffer(resources.Renderbuffer);
            _eglDestroyImageKHR?.Invoke(_eglDisplay, resources.EglImage);
            _logger?.LogDebug("Cleaned up GL resources for DMA buffer FD={Fd}", fd);
        }

        _dmaBufferResources.Clear();

        NativeEgl.MakeCurrent(_eglDisplay, 0, 0, 0);
        NativeEgl.DestroySurface(_eglDisplay, _eglDummySurface);
        NativeEgl.DestroyContext(_eglDisplay, _eglContext);
        NativeEgl.Terminate(_eglDisplay);

        _gl.Dispose();

        _gbmSurface?.Dispose();
        _logger?.LogDebug("Destroyed GBM surface");

        _gbmDevice?.Dispose();
        _logger?.LogDebug("Destroyed GBM device");

        _logger?.LogInformation("DRM ImGui renderer disposed");
    }

    private class DmaBufferGlResources
    {
        public required nint EglImage { get; init; }
        public required uint Renderbuffer { get; init; }
        public required uint Framebuffer { get; init; }
    }
}
