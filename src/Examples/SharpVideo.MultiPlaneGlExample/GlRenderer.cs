using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Silk.NET.OpenGLES;
using Microsoft.Extensions.Logging;
using SharpVideo.DmaBuffers;
using SharpVideo.Utils;

namespace SharpVideo.MultiPlaneGlExample;

/// <summary>
/// Hardware-accelerated OpenGL ES renderer using EGL with DMA-BUF render targets
/// Renders directly to DRM buffers for zero-copy presentation
/// </summary>
[SupportedOSPlatform("linux")]
public unsafe class GlRenderer : IDisposable
{
    private readonly ILogger? _logger;
    private readonly GL _gl;
    private readonly int _width;
    private readonly int _height;

    // DRM file descriptor
    private readonly int _drmFd;

    // GBM device and surface
    private readonly nint _gbmDevice;
    private readonly nint _gbmSurface;

    // EGL context
    private readonly nint _eglDisplay;
    private readonly nint _eglContext;
    private readonly nint _eglDummySurface;

    // EGL extension functions
    private readonly NativeEgl.EglCreateImageKHR? _eglCreateImageKHR;
    private readonly NativeEgl.EglDestroyImageKHR? _eglDestroyImageKHR;
    private readonly NativeEgl.GlEGLImageTargetRenderbufferStorageOES? _glEGLImageTargetRenderbufferStorageOES;

    // OpenGL resources
    private readonly uint _shaderProgram;
    private readonly uint _vao;
    private readonly uint _vbo;

    // Per-buffer OpenGL resources
    private readonly Dictionary<int, DmaBufferGlResources> _dmaBufferResources = new();

    private float _rotation = 0.0f;

    public GlRenderer(int width, int height, ILogger? logger = null)
    {
        _width = width;
        _height = height;
        _logger = logger;

        _logger?.LogInformation("Initializing EGL and OpenGL ES context...");

        // Open DRM device to get a file descriptor
        _drmFd = OpenDrmDevice();
        if (_drmFd < 0)
        {
            throw new Exception("Failed to open DRM device");
        }
        _logger?.LogDebug("Opened DRM device with FD: {Fd}", _drmFd);

        // Create GBM device from DRM fd (following kmscube approach)
        _gbmDevice = NativeGbm.CreateDevice(_drmFd);
        if (_gbmDevice == 0)
        {
            SharpVideo.Linux.Native.Libc.close(_drmFd);
            throw new Exception("Failed to create GBM device");
        }
        _logger?.LogDebug("Created GBM device: 0x{Device:X}", _gbmDevice);

        // Create GBM surface for rendering (following kmscube approach)
        _gbmSurface = NativeGbm.CreateSurface(
            _gbmDevice,
            (uint)width,
            (uint)height,
            NativeGbm.GBM_FORMAT_XRGB8888,
            NativeGbm.GBM_BO_USE_SCANOUT | NativeGbm.GBM_BO_USE_RENDERING);

        if (_gbmSurface == 0)
        {
            NativeGbm.DestroyDevice(_gbmDevice);
            SharpVideo.Linux.Native.Libc.close(_drmFd);
            throw new Exception("Failed to create GBM surface");
        }
        _logger?.LogDebug("Created GBM surface: 0x{Surface:X}", _gbmSurface);

        // Get EGL display using GBM platform (kmscube approach)
        _eglDisplay = GetEglDisplayFromGbm(_gbmDevice);

        if (_eglDisplay == 0 || _eglDisplay == NativeEgl.EGL_NO_DISPLAY)
        {
            NativeGbm.DestroyDevice(_gbmDevice);
            SharpVideo.Linux.Native.Libc.close(_drmFd);
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

            NativeGbm.DestroyDevice(_gbmDevice);
            SharpVideo.Linux.Native.Libc.close(_drmFd);
            throw new Exception($"Failed to initialize EGL: {errorMsg} (error code: 0x{error:X})");
        }

        _logger?.LogInformation("✓ EGL initialized: version {Major}.{Minor}", major, minor);

        // Log EGL information (following kmscube)
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

        // Choose config (following kmscube approach)
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

        var config = ChooseConfigMatchingVisual(_eglDisplay, configAttribs, NativeGbm.GBM_FORMAT_XRGB8888);
        _logger?.LogInformation("EGL config chosen");

        // Bind OpenGL ES API
        if (!NativeEgl.BindAPI(NativeEgl.EGL_OPENGL_ES_API))
        {
            var error = NativeEgl.GetError();
            throw new Exception($"Failed to bind OpenGL ES API: {NativeEgl.GetErrorString(error)}");
        }

        // Create context (OpenGL ES 2.0, matching kmscube)
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

        // Create EGL window surface from GBM surface (following kmscube approach)
        _eglDummySurface = NativeEgl.CreateWindowSurface(_eglDisplay, config, _gbmSurface, null);
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

        // Create shader program
        _shaderProgram = CreateShaderProgram();

        // Create vertex data for a rotating triangle
        (_vao, _vbo) = CreateTriangle();

        _logger?.LogInformation("OpenGL ES renderer initialized successfully");
    }

    /// <summary>
    /// Opens a DRM render device (following kmscube approach)
    /// </summary>
    private int OpenDrmDevice()
    {
        // Try render nodes first (preferred for pure GPU operations)
        for (int i = 128; i < 192; i++)
        {
            var devicePath = $"/dev/dri/renderD{i}";
            var fd = SharpVideo.Linux.Native.Libc.open(
                devicePath,
                SharpVideo.Linux.Native.OpenFlags.O_RDWR | SharpVideo.Linux.Native.OpenFlags.O_CLOEXEC);

            if (fd >= 0)
            {
                _logger?.LogDebug("Opened DRM render device: {DevicePath}", devicePath);
                return fd;
            }
        }

        // Fallback to card0 if no render node available
        var cardPath = "/dev/dri/card0";
        var cardFd = SharpVideo.Linux.Native.Libc.open(
            cardPath,
            SharpVideo.Linux.Native.OpenFlags.O_RDWR | SharpVideo.Linux.Native.OpenFlags.O_CLOEXEC);

        if (cardFd >= 0)
        {
            _logger?.LogDebug("Opened DRM card device: {DevicePath}", cardPath);
            return cardFd;
        }

        _logger?.LogError("Failed to open any DRM device");
        return -1;
    }

    /// <summary>
    /// Chooses an EGL config matching the GBM visual format (following kmscube approach)
    /// </summary>
    private nint ChooseConfigMatchingVisual(nint display, int[] attribs, uint visualId)
    {
        // First, get all configs matching our requirements
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
    /// Gets EGL display from GBM device (following kmscube approach)
    /// </summary>
    private nint GetEglDisplayFromGbm(nint gbmDevice)
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

                var display = eglGetPlatformDisplayEXT(NativeEgl.EGL_PLATFORM_GBM_KHR, gbmDevice, null);
                if (display != 0 && display != NativeEgl.EGL_NO_DISPLAY)
                {
                    _logger?.LogInformation("✓ Got EGL display using eglGetPlatformDisplayEXT with GBM platform");
                    return display;
                }
            }
        }

        // Fallback to eglGetDisplay with GBM device cast to EGLNativeDisplayType
        _logger?.LogDebug("Falling back to eglGetDisplay with GBM device");
        var fallbackDisplay = NativeEgl.GetDisplay(gbmDevice);
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
    gl_FragColor = vec4(vColor, 0.75); // 75% opacity for compositing
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
            // Position  // Color
            0.0f, 0.6f, 0.0f, 1.0f, 0.0f, 0.0f, // Top - Red
            -0.5f, -0.3f, 0.0f, 0.0f, 1.0f, 0.0f, // Bottom-left - Green
            0.5f, -0.3f, 0.0f, 0.0f, 0.0f, 1.0f // Bottom-right - Blue
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
    /// Renders a frame directly to the DMA buffer using OpenGL ES
    /// </summary>
    public void RenderToDmaBuffer(SharedDmaBuffer dmaBuffer)
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

        // Ensure rendering is complete
        _gl.Finish();

        // Update rotation for next frame
        _rotation += 0.02f;
        if (_rotation > MathF.PI * 2)
        {
            _rotation -= MathF.PI * 2;
        }
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
        _logger?.LogInformation("Disposing OpenGL ES renderer...");

        // Cleanup per-buffer resources
        foreach (var (fd, resources) in _dmaBufferResources)
        {
            _gl.DeleteFramebuffer(resources.Framebuffer);
            _gl.DeleteRenderbuffer(resources.Renderbuffer);
            _eglDestroyImageKHR?.Invoke(_eglDisplay, resources.EglImage);
            _logger?.LogDebug("Cleaned up GL resources for DMA buffer FD={Fd}", fd);
        }

        _dmaBufferResources.Clear();

        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteProgram(_shaderProgram);

        NativeEgl.MakeCurrent(_eglDisplay, 0, 0, 0);
        NativeEgl.DestroySurface(_eglDisplay, _eglDummySurface);
        NativeEgl.DestroyContext(_eglDisplay, _eglContext);
        NativeEgl.Terminate(_eglDisplay);

        _gl.Dispose();

        // Cleanup GBM surface
        if (_gbmSurface != 0)
        {
            NativeGbm.DestroySurface(_gbmSurface);
            _logger?.LogDebug("Destroyed GBM surface");
        }

        // Cleanup GBM device
        if (_gbmDevice != 0)
        {
            NativeGbm.DestroyDevice(_gbmDevice);
            _logger?.LogDebug("Destroyed GBM device");
        }

        // Close DRM file descriptor
        if (_drmFd >= 0)
        {
            SharpVideo.Linux.Native.Libc.close(_drmFd);
            _logger?.LogDebug("Closed DRM file descriptor");
        }

        _logger?.LogInformation("OpenGL ES renderer disposed");
    }

    private class DmaBufferGlResources
    {
        public required nint EglImage { get; init; }
        public required uint Renderbuffer { get; init; }
        public required uint Framebuffer { get; init; }
    }
}
