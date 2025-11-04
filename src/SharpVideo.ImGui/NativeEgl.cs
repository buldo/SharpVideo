using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.ImGui;

/// <summary>
/// Native EGL bindings for creating OpenGL ES context with DMA-BUF support
/// </summary>
[SupportedOSPlatform("linux")]
internal static unsafe class NativeEgl
{
    private const string LibEgl = "libEGL.so.1";

    // EGL Types
    public const int EGL_SUCCESS = 0x3000;
    public const int EGL_NOT_INITIALIZED = 0x3001;
    public const int EGL_BAD_ACCESS = 0x3002;
    public const int EGL_BAD_ALLOC = 0x3003;
    public const int EGL_BAD_ATTRIBUTE = 0x3004;
    public const int EGL_BAD_CONFIG = 0x3005;
    public const int EGL_BAD_CONTEXT = 0x3006;
    public const int EGL_BAD_CURRENT_SURFACE = 0x3007;
    public const int EGL_BAD_DISPLAY = 0x3008;
    public const int EGL_BAD_MATCH = 0x3009;
    public const int EGL_BAD_NATIVE_PIXMAP = 0x300A;
    public const int EGL_BAD_NATIVE_WINDOW = 0x300B;
    public const int EGL_BAD_PARAMETER = 0x300C;
    public const int EGL_BAD_SURFACE = 0x300D;

    // EGL Constants
    public const int EGL_NONE = 0x3038;
    public const int EGL_DEFAULT_DISPLAY = 0;
    public const int EGL_NO_CONTEXT = 0;
    public const int EGL_NO_SURFACE = 0;
    public const int EGL_NO_DISPLAY = 0;

    // Query string names
    public const int EGL_EXTENSIONS = 0x3055;
    public const int EGL_VENDOR = 0x3053;
    public const int EGL_VERSION = 0x3054;

    // Config attributes
    public const int EGL_RED_SIZE = 0x3024;
    public const int EGL_GREEN_SIZE = 0x3023;
    public const int EGL_BLUE_SIZE = 0x3022;
    public const int EGL_ALPHA_SIZE = 0x3021;
    public const int EGL_DEPTH_SIZE = 0x3025;
    public const int EGL_STENCIL_SIZE = 0x3026;
    public const int EGL_SURFACE_TYPE = 0x3033;
    public const int EGL_RENDERABLE_TYPE = 0x3040;
    public const int EGL_CONFORMANT = 0x3042;
    public const int EGL_SAMPLES = 0x3031;
    public const int EGL_NATIVE_VISUAL_ID = 0x302E;

    // Surface types
    public const int EGL_PBUFFER_BIT = 0x0001;
    public const int EGL_WINDOW_BIT = 0x0004;

    // API types
    public const int EGL_OPENGL_ES2_BIT = 0x0004;
    public const int EGL_OPENGL_ES3_BIT = 0x0040;
    public const int EGL_OPENGL_ES_API = 0x30A0;

    // Context attributes
    public const int EGL_CONTEXT_CLIENT_VERSION = 0x3098;
    public const int EGL_CONTEXT_MAJOR_VERSION = 0x3098;
    public const int EGL_CONTEXT_MINOR_VERSION = 0x30FB;

    // Pbuffer attributes
    public const int EGL_WIDTH = 0x3057;
    public const int EGL_HEIGHT = 0x3056;

    // EGL_EXT_image_dma_buf_import
    public const int EGL_LINUX_DMA_BUF_EXT = 0x3270;
    public const int EGL_LINUX_DRM_FOURCC_EXT = 0x3271;
    public const int EGL_DMA_BUF_PLANE0_FD_EXT = 0x3272;
    public const int EGL_DMA_BUF_PLANE0_OFFSET_EXT = 0x3273;
    public const int EGL_DMA_BUF_PLANE0_PITCH_EXT = 0x3274;
    public const int EGL_DMA_BUF_PLANE0_MODIFIER_LO_EXT = 0x3443;
    public const int EGL_DMA_BUF_PLANE0_MODIFIER_HI_EXT = 0x3444;

    // DRM formats (fourcc codes)
    public const uint DRM_FORMAT_ARGB8888 = 0x34325241; // 'AR24'
    public const uint DRM_FORMAT_XRGB8888 = 0x34325258; // 'XR24'

    // EGL_NO_IMAGE
    public static readonly nint EGL_NO_IMAGE = nint.Zero;

    // EGL Platform extensions (EGL 1.5 / EXT_platform_base)
    public const int EGL_PLATFORM_GBM_KHR = 0x31D7;
    public const int EGL_PLATFORM_WAYLAND_KHR = 0x31D8;
    public const int EGL_PLATFORM_X11_KHR = 0x31D9;
    public const int EGL_PLATFORM_DEVICE_EXT = 0x313F;

    [DllImport(LibEgl, EntryPoint = "eglGetDisplay")]
    public static extern nint GetDisplay(nint display_id);

    [DllImport(LibEgl, EntryPoint = "eglInitialize")]
    public static extern bool Initialize(nint dpy, out int major, out int minor);

    [DllImport(LibEgl, EntryPoint = "eglTerminate")]
    public static extern bool Terminate(nint dpy);

    [DllImport(LibEgl, EntryPoint = "eglGetError")]
    public static extern int GetError();

    [DllImport(LibEgl, EntryPoint = "eglChooseConfig")]
    public static extern bool ChooseConfig(nint dpy, int* attrib_list, nint* configs, int config_size,
        out int num_config);

    [DllImport(LibEgl, EntryPoint = "eglGetConfigAttrib")]
    public static extern bool GetConfigAttrib(nint dpy, nint config, int attribute, out int value);

    [DllImport(LibEgl, EntryPoint = "eglBindAPI")]
    public static extern bool BindAPI(int api);

    [DllImport(LibEgl, EntryPoint = "eglCreateContext")]
    public static extern nint CreateContext(nint dpy, nint config, nint share_context, int* attrib_list);

    [DllImport(LibEgl, EntryPoint = "eglDestroyContext")]
    public static extern bool DestroyContext(nint dpy, nint ctx);

    [DllImport(LibEgl, EntryPoint = "eglCreatePbufferSurface")]
    public static extern nint CreatePbufferSurface(nint dpy, nint config, int* attrib_list);

    [DllImport(LibEgl, EntryPoint = "eglCreateWindowSurface")]
    public static extern nint CreateWindowSurface(nint dpy, nint config, nint win, int* attrib_list);

    [DllImport(LibEgl, EntryPoint = "eglDestroySurface")]
    public static extern bool DestroySurface(nint dpy, nint surface);

    [DllImport(LibEgl, EntryPoint = "eglMakeCurrent")]
    public static extern bool MakeCurrent(nint dpy, nint draw, nint read, nint ctx);

    [DllImport(LibEgl, EntryPoint = "eglSwapBuffers")]
    public static extern bool SwapBuffers(nint dpy, nint surface);

    [DllImport(LibEgl, EntryPoint = "eglGetProcAddress")]
    public static extern nint GetProcAddress([MarshalAs(UnmanagedType.LPStr)] string procname);

    [DllImport(LibEgl, EntryPoint = "eglQueryString")]
    public static extern nint QueryString(nint dpy, int name);

    // Extension functions (loaded dynamically)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate nint EglCreateImageKHR(nint dpy, nint ctx, int target, nint buffer, int* attrib_list);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate bool EglDestroyImageKHR(nint dpy, nint image);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate nint EglGetPlatformDisplayEXT(int platform, nint native_display, int* attrib_list);

    // GL ES function for binding EGLImage to renderbuffer
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void GlEGLImageTargetRenderbufferStorageOES(uint target, nint image);

    public static string GetErrorString(int error)
    {
        return error switch
        {
            EGL_SUCCESS => "EGL_SUCCESS",
            EGL_NOT_INITIALIZED => "EGL_NOT_INITIALIZED",
            EGL_BAD_ACCESS => "EGL_BAD_ACCESS",
            EGL_BAD_ALLOC => "EGL_BAD_ALLOC",
            EGL_BAD_ATTRIBUTE => "EGL_BAD_ATTRIBUTE",
            EGL_BAD_CONFIG => "EGL_BAD_CONFIG",
            EGL_BAD_CONTEXT => "EGL_BAD_CONTEXT",
            EGL_BAD_CURRENT_SURFACE => "EGL_BAD_CURRENT_SURFACE",
            EGL_BAD_DISPLAY => "EGL_BAD_DISPLAY",
            EGL_BAD_MATCH => "EGL_BAD_MATCH",
            EGL_BAD_NATIVE_PIXMAP => "EGL_BAD_NATIVE_PIXMAP",
            EGL_BAD_NATIVE_WINDOW => "EGL_BAD_NATIVE_WINDOW",
            EGL_BAD_PARAMETER => "EGL_BAD_PARAMETER",
            EGL_BAD_SURFACE => "EGL_BAD_SURFACE",
            _ => $"Unknown error: 0x{error:X}"
        };
    }
}
