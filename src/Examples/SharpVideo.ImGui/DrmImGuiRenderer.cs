using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Silk.NET.OpenGLES;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Gbm;

namespace SharpVideo.ImGui;

/// <summary>
/// Hardware-accelerated OpenGL ES renderer for ImGui using GBM surface.
/// Renders ImGui directly to GBM surface for page flip presentation without vsync limitations.
/// </summary>
[SupportedOSPlatform("linux")]
public unsafe class DrmImGuiRenderer : IDisposable
{
  private readonly ILogger _logger;
    private readonly GL _gl;
   private readonly int _width;
    private readonly int _height;

    // EGL context
    private readonly nint _eglDisplay;
    private readonly nint _eglContext;
    private readonly nint _eglSurface;

    public DrmImGuiRenderer(
   GbmDevice gbmDevice,
        nint gbmSurfaceHandle,
        int width,
  int height,
        ILogger logger)
    {
        _width = width;
        _height = height;
  _logger = logger;

  _logger.LogInformation("Initializing DRM ImGui renderer with EGL and OpenGL ES context...");

        // Get EGL display using GBM platform
        _eglDisplay = GetEglDisplayFromGbm(gbmDevice);

if (_eglDisplay == 0 || _eglDisplay == NativeEgl.EGL_NO_DISPLAY)
        {
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
     _eglSurface = NativeEgl.CreateWindowSurface(_eglDisplay, config, gbmSurfaceHandle, null);
        if (_eglSurface == 0 || _eglSurface == NativeEgl.EGL_NO_SURFACE)
        {
      var error = NativeEgl.GetError();
  throw new Exception($"Failed to create EGL window surface: {NativeEgl.GetErrorString(error)}");
        }

        _logger?.LogInformation("EGL window surface created from GBM surface");

        // Make context current
        if (!NativeEgl.MakeCurrent(_eglDisplay, _eglSurface, _eglSurface, _eglContext))
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
    /// Renders ImGui draw data to the GBM surface using OpenGL ES.
    /// After calling this, you need to call eglSwapBuffers or handle the GBM buffer swap.
    /// </summary>
public void RenderToGbmSurface(Hexa.NET.ImGui.ImDrawDataPtr drawData)
    {
   _logger?.LogDebug("RenderToGbmSurface: START");
   
      // Make sure we're rendering to the correct surface
        _logger?.LogDebug("RenderToGbmSurface: Making EGL context current...");
        if (!NativeEgl.MakeCurrent(_eglDisplay, _eglSurface, _eglSurface, _eglContext))
{
            var error = NativeEgl.GetError();
    _logger?.LogError("Failed to make EGL context current: {Error}", NativeEgl.GetErrorString(error));
  return;
 }
 _logger?.LogDebug("RenderToGbmSurface: EGL context is current");

        _logger?.LogDebug("RenderToGbmSurface: Setting viewport...");
     _gl.Viewport(0, 0, (uint)_width, (uint)_height);
   _logger?.LogDebug("RenderToGbmSurface: Viewport set");

// Clear with transparent black background
        _logger?.LogDebug("RenderToGbmSurface: Clearing...");
        _gl.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
_gl.Clear(ClearBufferMask.ColorBufferBit);
        _logger?.LogDebug("RenderToGbmSurface: Cleared");

 // Enable blending for ImGui
      _logger?.LogDebug("RenderToGbmSurface: Setting up blending...");
 _gl.Enable(EnableCap.Blend);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
   _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
    _gl.Disable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.ScissorTest);
        _logger?.LogDebug("RenderToGbmSurface: Blending setup complete");

        // Render ImGui draw data using ImGui OpenGL3 backend
        _logger?.LogDebug("RenderToGbmSurface: Calling ImGuiImplOpenGL3.RenderDrawData...");
        Hexa.NET.ImGui.Backends.OpenGL3.ImGuiImplOpenGL3.RenderDrawData(drawData);
        _logger?.LogDebug("RenderToGbmSurface: ImGui render complete");

        // Swap EGL buffers (this will prepare the next GBM BO)
        _logger?.LogDebug("RenderToGbmSurface: Swapping EGL buffers...");
        if (!NativeEgl.SwapBuffers(_eglDisplay, _eglSurface))
    {
      var error = NativeEgl.GetError();
_logger?.LogWarning("eglSwapBuffers failed: {Error}", NativeEgl.GetErrorString(error));
        }
        _logger?.LogDebug("RenderToGbmSurface: EGL buffers swapped");
_logger?.LogDebug("RenderToGbmSurface: COMPLETE");
  }

    public void Dispose()
    {
   _logger?.LogInformation("Disposing DRM ImGui renderer...");

     NativeEgl.MakeCurrent(_eglDisplay, 0, 0, 0);
      NativeEgl.DestroySurface(_eglDisplay, _eglSurface);
     NativeEgl.DestroyContext(_eglDisplay, _eglContext);
        NativeEgl.Terminate(_eglDisplay);

     _gl.Dispose();

        _logger?.LogInformation("DRM ImGui renderer disposed");
    }
}
