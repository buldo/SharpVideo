using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Silk.NET.OpenGLES;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Linux.Native.Gbm;

namespace SharpVideo.MultiPlaneGlExample;

/// <summary>
/// OpenGL ES renderer that renders directly to a GBM surface.
/// This is the standard kmscube/ImGui approach where OpenGL ES renders to
/// the GBM surface and gbm_surface_lock_front_buffer() returns the BO for scanout.
/// </summary>
[SupportedOSPlatform("linux")]
public unsafe class GlSurfaceRenderer : IDisposable
{
    private readonly ILogger? _logger;
    private readonly GL _gl;
    private readonly int _width;
    private readonly int _height;

    // EGL context
    private readonly nint _eglDisplay;
    private readonly nint _eglContext;
    private readonly nint _eglSurface;

    // OpenGL resources
    private readonly uint _shaderProgram;
    private readonly uint _vao;
    private readonly uint _vbo;

    private float _rotation = 0.0f;

    public GlSurfaceRenderer(
        DrmDevice drmDevice,
        GbmDevice gbmDevice,
        GbmSurface gbmSurface,
        int width,
        int height,
        ILogger? logger)
    {
        _width = width;
        _height = height;
        _logger = logger;

        _logger?.LogInformation("Initializing EGL and OpenGL ES context with GBM surface...");

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
            throw new Exception($"Failed to initialize EGL: {errorMsg} (error code: 0x{error:X})");
        }

        _logger?.LogInformation("✓ EGL initialized: version {Major}.{Minor}", major, minor);

        // Log EGL information
        var eglVendorPtr = NativeEgl.QueryString(_eglDisplay, NativeEgl.EGL_VENDOR);
        if (eglVendorPtr != 0)
        {
            var eglVendor = Marshal.PtrToStringAnsi(eglVendorPtr);
            _logger?.LogDebug("EGL vendor: {Vendor}", eglVendor);
        }

        // Choose config (following kmscube approach)
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

        var config = ChooseConfigMatchingVisual(_eglDisplay, configAttribs, KnownPixelFormats.DRM_FORMAT_ARGB8888.Fourcc);
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
        _eglSurface = NativeEgl.CreateWindowSurface(_eglDisplay, config, gbmSurface.Handle, null);
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

        _logger?.LogInformation("GL Vendor: {Vendor}", vendor);
        _logger?.LogInformation("GL Renderer: {Renderer}", renderer);
        _logger?.LogInformation("GL Version: {Version}", version);

        // Create shader program
        _shaderProgram = CreateShaderProgram();

        // Create vertex data for a rotating triangle
        (_vao, _vbo) = CreateTriangle();

        _logger?.LogInformation("OpenGL ES renderer initialized successfully");
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
                    throw new Exception($"No EGL configs with appropriate attributes: {NativeEgl.GetErrorString(error)}");
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
        // Query client extensions first
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

        // Fallback to eglGetDisplay
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

    private uint CreateShaderProgram()
    {
        const string vertexShaderSource = @"
attribute vec3 aPosition;
attribute vec3 aColor;

uniform mat4 uTransform;

varying vec3 vColor;

void main()
{
    gl_Position = uTransform * vec4(aPosition, 1.0);
    vColor = aColor;
}";

        const string fragmentShaderSource = @"
precision mediump float;

varying vec3 vColor;

void main()
{
    gl_FragColor = vec4(vColor, 0.75);
}";

        // Compile vertex shader
        var vertexShader = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vertexShader, vertexShaderSource);
        _gl.CompileShader(vertexShader);
        CheckShaderCompilation(vertexShader, "Vertex");

        // Compile fragment shader
        var fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragmentShader, fragmentShaderSource);
        _gl.CompileShader(fragmentShader);
        CheckShaderCompilation(fragmentShader, "Fragment");

        // Link program
        var program = _gl.CreateProgram();
        _gl.AttachShader(program, vertexShader);
        _gl.AttachShader(program, fragmentShader);
        _gl.LinkProgram(program);
        CheckProgramLinking(program);

        // Cleanup shaders
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        _logger?.LogInformation("Shader program created and linked");

        return program;
    }

    private void CheckShaderCompilation(uint shader, string type)
    {
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int success);
        if (success == 0)
        {
            var log = _gl.GetShaderInfoLog(shader);
            throw new Exception($"{type} shader compilation failed: {log}");
        }
    }

    private void CheckProgramLinking(uint program)
    {
        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int success);
        if (success == 0)
        {
            var log = _gl.GetProgramInfoLog(program);
            throw new Exception($"Shader program linking failed: {log}");
        }
    }

    private (uint vao, uint vbo) CreateTriangle()
    {
        // Triangle vertices: position (x, y, z) and color (r, g, b)
        float[] vertices =
        [
            // Position             // Color
            0.0f, 0.6f, 0.0f,       1.0f, 0.0f, 0.0f,   // Top - Red
            -0.5f, -0.3f, 0.0f,     0.0f, 1.0f, 0.0f,   // Bottom-left - Green
            0.5f, -0.3f, 0.0f,      0.0f, 0.0f, 1.0f    // Bottom-right - Blue
        ];

        var vao = _gl.GenVertexArray();
        _gl.BindVertexArray(vao);

        var vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        fixed (float* v = vertices)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)),
                v, BufferUsageARB.StaticDraw);
        }

        // Position attribute
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
        _gl.EnableVertexAttribArray(0);

        // Color attribute
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float),
            (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(1);

        _logger?.LogInformation("Triangle geometry created");

        return (vao, vbo);
    }

    /// <summary>
    /// Renders a frame to the GBM surface.
    /// After calling this, use gbm_surface_lock_front_buffer() to get the BO for scanout.
    /// </summary>
    public void RenderFrame(int frameNumber)
    {
        // Set viewport
        _gl.Viewport(0, 0, (uint)_width, (uint)_height);

        // Clear with transparent background
        _gl.ClearColor(0.0f, 0.0f, 0.0f, 0.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        // Enable blending for transparency
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // Use shader program
        _gl.UseProgram(_shaderProgram);

        // Create rotation matrix
        var transform = Matrix4x4.CreateRotationZ(_rotation);
        var transformLocation = _gl.GetUniformLocation(_shaderProgram, "uTransform");

        Span<float> matrixData = stackalloc float[16];
        matrixData[0] = transform.M11;
        matrixData[1] = transform.M12;
        matrixData[2] = transform.M13;
        matrixData[3] = transform.M14;
        matrixData[4] = transform.M21;
        matrixData[5] = transform.M22;
        matrixData[6] = transform.M23;
        matrixData[7] = transform.M24;
        matrixData[8] = transform.M31;
        matrixData[9] = transform.M32;
        matrixData[10] = transform.M33;
        matrixData[11] = transform.M34;
        matrixData[12] = transform.M41;
        matrixData[13] = transform.M42;
        matrixData[14] = transform.M43;
        matrixData[15] = transform.M44;

        fixed (float* ptr = matrixData)
        {
            _gl.UniformMatrix4(transformLocation, 1, false, ptr);
        }

        // Draw triangle
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);

        // Swap buffers - this presents the rendered content to the GBM surface
        // After this, gbm_surface_lock_front_buffer() will return the rendered BO
        if (!NativeEgl.SwapBuffers(_eglDisplay, _eglSurface))
        {
            var error = NativeEgl.GetError();
            _logger?.LogWarning("eglSwapBuffers failed: {Error}", NativeEgl.GetErrorString(error));
        }

        // Update rotation for next frame
        _rotation += 0.02f;
        if (_rotation > MathF.PI * 2)
        {
            _rotation -= MathF.PI * 2;
        }
    }

    public void Dispose()
    {
        _logger?.LogInformation("Disposing OpenGL ES renderer...");

        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteProgram(_shaderProgram);

        NativeEgl.MakeCurrent(_eglDisplay, 0, 0, 0);
        NativeEgl.DestroySurface(_eglDisplay, _eglSurface);
        NativeEgl.DestroyContext(_eglDisplay, _eglContext);
        NativeEgl.Terminate(_eglDisplay);

        _gl.Dispose();

        _logger?.LogInformation("OpenGL ES renderer disposed");
    }
}
