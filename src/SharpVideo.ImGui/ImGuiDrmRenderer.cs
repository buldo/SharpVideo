using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGLES;
using SharpVideo.Gbm;

namespace SharpVideo.ImGui;

/// <summary>
/// Hardware-accelerated OpenGL ES renderer for ImGui using GBM surface.
/// Renders ImGui directly to GBM surface for DRM page flip presentation.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe class ImGuiDrmRenderer : IDisposable
{
    private readonly ILogger? _logger;
    private readonly GL _gl;
    private readonly uint _width;
    private readonly uint _height;

    // EGL context
    private readonly nint _eglDisplay;
    private readonly nint _eglContext;
    private readonly nint _eglSurface;

    private bool _disposed;

    public ImGuiDrmRenderer(
        ImGuiDrmConfiguration config,
        ILogger? logger = null)
    {
        config.Validate();

        _width = config.Width;
        _height = config.Height;
        _logger = logger;

        _logger?.LogDebug("Initializing ImGui DRM renderer (OpenGL ES + EGL)");

        // Get EGL display using GBM platform
        _eglDisplay = GetEglDisplayFromGbm(config.GbmDevice);

        if (_eglDisplay == 0 || _eglDisplay == NativeEgl.EGL_NO_DISPLAY)
        {
            throw new InvalidOperationException("Failed to get EGL display from GBM device");
        }

        _logger?.LogDebug("EGL display obtained from GBM: 0x{Display:X}", _eglDisplay);

        // Initialize EGL
        if (!NativeEgl.Initialize(_eglDisplay, out int major, out int minor))
        {
            var error = NativeEgl.GetError();
            var errorMsg = NativeEgl.GetErrorString(error);
            throw new InvalidOperationException($"Failed to initialize EGL: {errorMsg} (0x{error:X})");
        }

        _logger?.LogInformation("EGL initialized: version {Major}.{Minor}", major, minor);

        // Log EGL information
        LogEglInfo();

        // Choose EGL config
        int[] configAttribs =
        [
            NativeEgl.EGL_SURFACE_TYPE, NativeEgl.EGL_WINDOW_BIT,
            NativeEgl.EGL_RED_SIZE, 8,
            NativeEgl.EGL_GREEN_SIZE, 8,
            NativeEgl.EGL_BLUE_SIZE, 8,
            NativeEgl.EGL_ALPHA_SIZE, 8,
            NativeEgl.EGL_RENDERABLE_TYPE, NativeEgl.EGL_OPENGL_ES2_BIT,
            NativeEgl.EGL_SAMPLES, 0,
            NativeEgl.EGL_NONE
        ];

        var eglConfig = ChooseEglConfig(_eglDisplay, configAttribs, config.PixelFormat.Fourcc);
        _logger?.LogDebug("EGL config selected");

        // Bind OpenGL ES API
        if (!NativeEgl.BindAPI(NativeEgl.EGL_OPENGL_ES_API))
        {
            var error = NativeEgl.GetError();
            throw new InvalidOperationException($"Failed to bind OpenGL ES API: {NativeEgl.GetErrorString(error)}");
        }

        // Create EGL context (OpenGL ES 2.0)
        int[] contextAttribs =
        [
            NativeEgl.EGL_CONTEXT_CLIENT_VERSION, 2,
            NativeEgl.EGL_NONE
        ];

        unsafe
        {
            fixed (int* contextAttribsPtr = contextAttribs)
            {
                _eglContext = NativeEgl.CreateContext(_eglDisplay, eglConfig, NativeEgl.EGL_NO_CONTEXT, contextAttribsPtr);
                if (_eglContext == 0)
                {
                    var error = NativeEgl.GetError();
                    throw new InvalidOperationException($"Failed to create EGL context: {NativeEgl.GetErrorString(error)}");
                }
            }
        }

        _logger?.LogDebug("EGL context created");

        // Create EGL window surface from GBM surface
        _eglSurface = NativeEgl.CreateWindowSurface(_eglDisplay, eglConfig, config.GbmSurfaceHandle, null);
        if (_eglSurface == 0 || _eglSurface == NativeEgl.EGL_NO_SURFACE)
        {
            var error = NativeEgl.GetError();
            throw new InvalidOperationException($"Failed to create EGL window surface: {NativeEgl.GetErrorString(error)}");
        }

        _logger?.LogDebug("EGL window surface created from GBM surface");

        // Make context current
        if (!NativeEgl.MakeCurrent(_eglDisplay, _eglSurface, _eglSurface, _eglContext))
        {
            var error = NativeEgl.GetError();
            throw new InvalidOperationException($"Failed to make EGL context current: {NativeEgl.GetErrorString(error)}");
        }

        // Initialize OpenGL ES
        _gl = GL.GetApi(NativeEgl.GetProcAddress);
        
        // Log GL info
        LogGlInfo();

        _logger?.LogInformation("ImGui DRM renderer initialized successfully");
    }

    /// <summary>
    /// Renders ImGui draw data to the GBM surface using OpenGL ES.
    /// This only performs rendering to the back buffer WITHOUT swapping.
    /// Call SwapBuffers() after to commit the frame.
    /// </summary>
    public void RenderDrawData(Hexa.NET.ImGui.ImDrawDataPtr drawData)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Ensure we have the correct EGL context current
        // This is necessary after ReleaseContext() was called
        AcquireContext();

        _gl.Viewport(0, 0, _width, _height);

        // Clear with black background
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
        Hexa.NET.ImGui.Backends.OpenGL3.ImGuiImplOpenGL3.RenderDrawData(drawData);
    }

    /// <summary>
    /// Swaps EGL buffers to commit the rendered frame.
    /// Returns true if successful, false if swap failed.
    /// Should only be called if the frame will actually be submitted to display.
    /// </summary>
    public bool SwapBuffers()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!NativeEgl.SwapBuffers(_eglDisplay, _eglSurface))
        {
            var error = NativeEgl.GetError();
            
            // EGL_BAD_SURFACE can occur if surface is already being used by GBM
            // This is expected in some race conditions and can be safely ignored
            if (error == NativeEgl.EGL_BAD_SURFACE)
            {
                _logger?.LogDebug("eglSwapBuffers failed: {Error} (expected in some scenarios)", 
                    NativeEgl.GetErrorString(error));
            }
            else
            {
                _logger?.LogWarning("eglSwapBuffers failed: {Error}", NativeEgl.GetErrorString(error));
            }
            
            return false;
        }
        return true;
    }

    /// <summary>
    /// Releases the EGL context from the current surface.
    /// This must be called after SwapBuffers() and before GBM surface operations
    /// to prevent EGL_BAD_SURFACE and EGL_BAD_ACCESS errors.
    /// </summary>
    public void ReleaseContext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Unbind the context from surfaces to allow GBM to lock the front buffer
        if (!NativeEgl.MakeCurrent(_eglDisplay, NativeEgl.EGL_NO_SURFACE, NativeEgl.EGL_NO_SURFACE, NativeEgl.EGL_NO_CONTEXT))
        {
            var error = NativeEgl.GetError();
            
            // EGL_BAD_ACCESS can occur if context is already released
            if (error != NativeEgl.EGL_BAD_ACCESS)
            {
                _logger?.LogWarning("Failed to release EGL context: {Error}", NativeEgl.GetErrorString(error));
            }
        }
    }

    /// <summary>
    /// Re-acquires the EGL context for the surface.
    /// This must be called before the next RenderDrawData() operation.
    /// </summary>
    public void AcquireContext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!NativeEgl.MakeCurrent(_eglDisplay, _eglSurface, _eglSurface, _eglContext))
        {
            var error = NativeEgl.GetError();
            
            // EGL_BAD_ACCESS can occur during shutdown or if surface is temporarily unavailable
            // EGL_BAD_SURFACE can occur if GBM is currently locking buffers
            if (error == NativeEgl.EGL_BAD_ACCESS || error == NativeEgl.EGL_BAD_SURFACE)
            {
                _logger?.LogDebug("Failed to make EGL context current: {Error} (may be transient)", 
                    NativeEgl.GetErrorString(error));
            }
            else
            {
                _logger?.LogError("Failed to make EGL context current: {Error}", NativeEgl.GetErrorString(error));
            }
        }
    }

    private unsafe nint ChooseEglConfig(nint display, int[] attribs, uint visualId)
    {
        fixed (int* attribsPtr = attribs)
        {
            if (!NativeEgl.ChooseConfig(display, attribsPtr, null, 0, out int count) || count == 0)
            {
                var error = NativeEgl.GetError();
                throw new InvalidOperationException($"No EGL configs available: {NativeEgl.GetErrorString(error)}");
            }

            var configs = new nint[count];
            fixed (nint* configsPtr = configs)
            {
                if (!NativeEgl.ChooseConfig(display, attribsPtr, configsPtr, count, out int matched) || matched == 0)
                {
                    var error = NativeEgl.GetError();
                    throw new InvalidOperationException($"No EGL configs with appropriate attributes: {NativeEgl.GetErrorString(error)}");
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

                // If no exact match, use the first config
                _logger?.LogDebug("No exact visual match found, using first config");
                return configs[0];
            }
        }
    }

    private nint GetEglDisplayFromGbm(GbmDevice gbmDevice)
    {
        // Query client extensions first
        var clientExtPtr = NativeEgl.QueryString(NativeEgl.EGL_NO_DISPLAY, NativeEgl.EGL_EXTENSIONS);
        string? clientExtensions = null;
        if (clientExtPtr != 0)
        {
            clientExtensions = Marshal.PtrToStringAnsi(clientExtPtr);
            _logger?.LogTrace("EGL client extensions: {Extensions}", clientExtensions);
        }

        // Try eglGetPlatformDisplayEXT if available (preferred)
        if (clientExtensions?.Contains("EGL_EXT_platform_base") == true)
        {
            var getPlatformDisplayPtr = NativeEgl.GetProcAddress("eglGetPlatformDisplayEXT");
            if (getPlatformDisplayPtr != 0)
            {
                var eglGetPlatformDisplayEXT =
                    Marshal.GetDelegateForFunctionPointer<NativeEgl.EglGetPlatformDisplayEXT>(getPlatformDisplayPtr);

                var display = eglGetPlatformDisplayEXT(NativeEgl.EGL_PLATFORM_GBM_KHR, gbmDevice.Fd, null);
                if (display != 0 && display != NativeEgl.EGL_NO_DISPLAY)
                {
                    _logger?.LogDebug("Got EGL display using eglGetPlatformDisplayEXT");
                    return display;
                }
            }
        }

        // Fallback to eglGetDisplay
        _logger?.LogDebug("Using eglGetDisplay with GBM device");
        var fallbackDisplay = NativeEgl.GetDisplay(gbmDevice.Fd);
        if (fallbackDisplay != 0 && fallbackDisplay != NativeEgl.EGL_NO_DISPLAY)
        {
            return fallbackDisplay;
        }

        throw new InvalidOperationException("Failed to get EGL display from GBM device");
    }

    private void LogEglInfo()
    {
        var vendorPtr = NativeEgl.QueryString(_eglDisplay, NativeEgl.EGL_VENDOR);
        var versionPtr = NativeEgl.QueryString(_eglDisplay, NativeEgl.EGL_VERSION);

        if (vendorPtr != 0)
        {
            var vendor = Marshal.PtrToStringAnsi(vendorPtr);
            _logger?.LogDebug("EGL vendor: {Vendor}", vendor);
        }

        if (versionPtr != 0)
        {
            var version = Marshal.PtrToStringAnsi(versionPtr);
            _logger?.LogDebug("EGL version: {Version}", version);
        }
    }

    private void LogGlInfo()
    {
        var vendor = _gl.GetStringS(StringName.Vendor);
        var renderer = _gl.GetStringS(StringName.Renderer);
        var version = _gl.GetStringS(StringName.Version);

        _logger?.LogDebug("GL vendor: {Vendor}", vendor);
        _logger?.LogDebug("GL renderer: {Renderer}", renderer);
        _logger?.LogDebug("GL version: {Version}", version);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _logger?.LogDebug("Disposing ImGui DRM renderer");

        // Step 1: Release context from any surfaces first
        try
        {
            if (!NativeEgl.MakeCurrent(_eglDisplay, NativeEgl.EGL_NO_SURFACE, NativeEgl.EGL_NO_SURFACE, NativeEgl.EGL_NO_CONTEXT))
            {
                var error = NativeEgl.GetError();
                _logger?.LogDebug("Context already released or error during release: {Error}", 
                    NativeEgl.GetErrorString(error));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Exception while releasing EGL context");
        }

        // Step 2: Destroy surface
        try
        {
            if (_eglSurface != 0 && _eglSurface != NativeEgl.EGL_NO_SURFACE)
            {
                NativeEgl.DestroySurface(_eglDisplay, _eglSurface);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Exception while destroying EGL surface");
        }

        // Step 3: Destroy context
        try
        {
            if (_eglContext != 0 && _eglContext != NativeEgl.EGL_NO_CONTEXT)
            {
                NativeEgl.DestroyContext(_eglDisplay, _eglContext);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Exception while destroying EGL context");
        }

        // Step 4: Terminate display
        try
        {
            if (_eglDisplay != 0 && _eglDisplay != NativeEgl.EGL_NO_DISPLAY)
            {
                NativeEgl.Terminate(_eglDisplay);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Exception while terminating EGL display");
        }

        // Step 5: Dispose GL
        try
        {
            _gl?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Exception while disposing GL");
        }

        _disposed = true;
        _logger?.LogDebug("ImGui DRM renderer disposed");
    }
}
