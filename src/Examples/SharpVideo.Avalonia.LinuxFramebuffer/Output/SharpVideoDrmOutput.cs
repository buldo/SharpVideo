using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Avalonia;
using Avalonia.LinuxFramebuffer.Output;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Egl;
using Avalonia.OpenGL.Surfaces;
using Avalonia.Platform;

using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Linux.Native.Gbm;
using SharpVideo.MultiPlaneGlExample;
using SharpVideo.Utils;

namespace SharpVideo.Avalonia.LinuxFramebuffer.Output;

[SupportedOSPlatform("linux")]
public unsafe class SharpVideoDrmOutput : IGlOutputBackend, IGlPlatformSurface, IDisposable
{
    private readonly DrmDevice _drmDevice;
    private readonly DrmPlaneDoubleBufferPresenter _presenter;
    private readonly GbmDevice _gbmDevice;
    private readonly GbmSurface _gbmDummySurface;

    private readonly EglDisplay _eglDisplay;
    private readonly EglContext _deferredContext;

    // EGL extension functions for DMA-BUF rendering
    private readonly NativeEgl.EglCreateImageKHR? _eglCreateImageKHR;
    private readonly NativeEgl.EglDestroyImageKHR? _eglDestroyImageKHR;
    private readonly NativeEgl.GlEGLImageTargetRenderbufferStorageOES? _glEGLImageTargetRenderbufferStorageOES;

    // Per-buffer OpenGL resources
    private readonly Dictionary<int, DmaBufferGlResources> _dmaBufferResources = new();

    private bool _disposed;

    [DllImport("libEGL.so.1")]
    static extern IntPtr eglGetProcAddress(string proc);

    public SharpVideoDrmOutput(
        DrmDevice drmDevice,
        DrmPlaneDoubleBufferPresenter presenter)
    {
        _drmDevice = drmDevice;
        _presenter = presenter;

        // Create GBM device for EGL initialization (similar to GlRenderer)
        _gbmDevice = GbmDevice.CreateFromDrmDevice(_drmDevice);

        // Create a dummy GBM surface for EGL context initialization
        _gbmDummySurface = _gbmDevice.CreateSurface(
            _presenter.Width, _presenter.Height,
            KnownPixelFormats.DRM_FORMAT_ARGB8888,
            GbmBoUse.GBM_BO_USE_RENDERING);

        // Initialize EGL display
        _eglDisplay = new EglDisplay(
            new EglDisplayCreationOptions
            {
                Egl = new EglInterface(eglGetProcAddress),
                PlatformType = 0x31D7, // EGL_PLATFORM_GBM_KHR
                PlatformDisplay = _gbmDevice.Fd,
                SupportsMultipleContexts = true,
                SupportsContextSharing = true
            });

        // Create EGL context
        _deferredContext = _eglDisplay.CreateContext(null);
        PlatformGraphics = new SharedContextGraphics(_deferredContext);

        // Load EGL extension functions for DMA-BUF support
        var createImagePtr = eglGetProcAddress("eglCreateImageKHR");
        var destroyImagePtr = eglGetProcAddress("eglDestroyImageKHR");
        var targetRenderbufferPtr = eglGetProcAddress("glEGLImageTargetRenderbufferStorageOES");

        if (createImagePtr == IntPtr.Zero || destroyImagePtr == IntPtr.Zero || targetRenderbufferPtr == IntPtr.Zero)
        {
            throw new Exception(
                "Required EGL/GL extensions not available (EGL_KHR_image_base, EGL_EXT_image_dma_buf_import, GL_OES_EGL_image)");
        }

        _eglCreateImageKHR = Marshal.GetDelegateForFunctionPointer<NativeEgl.EglCreateImageKHR>(createImagePtr);
        _eglDestroyImageKHR = Marshal.GetDelegateForFunctionPointer<NativeEgl.EglDestroyImageKHR>(destroyImagePtr);
        _glEGLImageTargetRenderbufferStorageOES =
            Marshal.GetDelegateForFunctionPointer<NativeEgl.GlEGLImageTargetRenderbufferStorageOES>(
                targetRenderbufferPtr);
    }

    public PixelSize PixelSize => new PixelSize((int)_presenter.Width, (int)_presenter.Height);

    public double Scaling { get; set; } = 1.0;

    public IPlatformGraphics PlatformGraphics { get; private set; }

    public IGlPlatformSurfaceRenderTarget CreateGlRenderTarget() => new RenderTarget(this);

    public IGlPlatformSurfaceRenderTarget CreateGlRenderTarget(IGlContext context)
    {
        if (context != _deferredContext)
            throw new InvalidOperationException(
                "This platform backend can only create render targets for its primary context");
        return CreateGlRenderTarget();
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
            EglConsts.EGL_WIDTH, (int)_presenter.Width, // EGL_WIDTH
            EglConsts.EGL_HEIGHT, (int)_presenter.Height, // EGL_HEIGHT
            NativeEgl.EGL_LINUX_DRM_FOURCC_EXT,  (int)KnownPixelFormats.DRM_FORMAT_ARGB8888.Fourcc, // EGL_LINUX_DRM_FOURCC_EXT, DRM_FORMAT_ARGB8888
            NativeEgl.EGL_DMA_BUF_PLANE0_FD_EXT, dmaBuffer.DmaBuffer.Fd, // EGL_DMA_BUF_PLANE0_FD_EXT
            NativeEgl.EGL_DMA_BUF_PLANE0_OFFSET_EXT, 0, // EGL_DMA_BUF_PLANE0_OFFSET_EXT
            NativeEgl.EGL_DMA_BUF_PLANE0_PITCH_EXT, (int)dmaBuffer.Stride, // EGL_DMA_BUF_PLANE0_PITCH_EXT
            NativeEgl.EGL_NONE // EGL_NONE
        ];

        IntPtr eglImage;
        fixed (int* attribsPtr = imageAttribs)
        {
            eglImage = _eglCreateImageKHR(
                _eglDisplay.Handle,
                IntPtr.Zero, // EGL_NO_CONTEXT
                0x3270, // EGL_LINUX_DMA_BUF_EXT
                IntPtr.Zero,
                attribsPtr);
        }

        if (eglImage == IntPtr.Zero)
        {
            throw new Exception($"Failed to create EGLImage from DMA-BUF");
        }

        // Create renderbuffer and bind EGLImage to it
        var gl = _deferredContext.GlInterface;
        var renderbuffer = gl.GenRenderbuffer();
        gl.BindRenderbuffer(GlConsts.GL_RENDERBUFFER, renderbuffer);
        _glEGLImageTargetRenderbufferStorageOES((uint)GlConsts.GL_RENDERBUFFER, eglImage);

        var glError = gl.GetError();
        if (glError != 0)
        {
            throw new Exception($"Failed to bind EGLImage to renderbuffer: {glError}");
        }

        // Create framebuffer and attach renderbuffer
        var framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(GlConsts.GL_FRAMEBUFFER, framebuffer);
        gl.FramebufferRenderbuffer(
            GlConsts.GL_FRAMEBUFFER,
            GlConsts.GL_COLOR_ATTACHMENT0,
            GlConsts.GL_RENDERBUFFER,
            renderbuffer);

        var status = gl.CheckFramebufferStatus(GlConsts.GL_FRAMEBUFFER);
        if (status != GlConsts.GL_FRAMEBUFFER_COMPLETE)
        {
            throw new Exception($"Framebuffer is not complete: {status}");
        }

        return new DmaBufferGlResources
        {
            EglImage = eglImage,
            Renderbuffer = renderbuffer,
            Framebuffer = framebuffer
        };
    }

    class RenderTarget : IGlPlatformSurfaceRenderTarget
    {
        private readonly SharpVideoDrmOutput _parent;

        public RenderTarget(SharpVideoDrmOutput parent)
        {
            _parent = parent;
        }

        public void Dispose()
        {
            // Nothing to dispose - we don't own the buffers
        }

        class RenderSession : IGlPlatformSurfaceRenderingSession
        {
            private readonly SharpVideoDrmOutput _parent;
            private readonly IDisposable _clearContext;
            private readonly SharedDmaBuffer _targetBuffer;
            private readonly DmaBufferGlResources _glResources;

            public RenderSession(SharpVideoDrmOutput parent, IDisposable clearContext)
            {
                _parent = parent;
                _clearContext = clearContext;

                // Get the back buffer from presenter
                _targetBuffer = _parent._presenter.GetPrimaryPlaneBackBufferDma();

                // Get or create GL resources for this DMA buffer
                if (!_parent._dmaBufferResources.TryGetValue(_targetBuffer.DmaBuffer.Fd, out _glResources))
                {
                    _glResources = _parent.CreateDmaBufferGlResources(_targetBuffer);
                    _parent._dmaBufferResources[_targetBuffer.DmaBuffer.Fd] = _glResources;
                }

                // Bind the framebuffer that renders to this DMA buffer
                var gl = _parent._deferredContext.GlInterface;
                gl.BindFramebuffer(GlConsts.GL_FRAMEBUFFER, _glResources.Framebuffer);
                gl.Viewport(0, 0, (int)_parent._presenter.Width, (int)_parent._presenter.Height);
            }

            public void Dispose()
            {
                _parent._deferredContext.GlInterface.Flush();
                // Present the rendered buffer through the presenter
                _parent._presenter.SwapPrimaryPlaneBuffers();

                _clearContext.Dispose();
            }

            public IGlContext Context => _parent._deferredContext;

            public PixelSize Size => new PixelSize((int)_parent._presenter.Width, (int)_parent._presenter.Height);

            public double Scaling => _parent.Scaling;

            public bool IsYFlipped => false;
        }

        public IGlPlatformSurfaceRenderingSession BeginDraw()
        {
            // Create a dummy EGL surface for context activation (we render to framebuffer)
            var dummySurface = _parent._eglDisplay.EglInterface.CreatePBufferSurface(
                _parent._eglDisplay.Handle,
                _parent._eglDisplay.Config,
                new[] { 0x3057, 16, 0x3056, 16, 0x3038 }); // EGL_WIDTH, EGL_HEIGHT, EGL_NONE

            var eglSurface = new EglSurface(_parent._eglDisplay, dummySurface);
            var contextToken = _parent._deferredContext.MakeCurrent(eglSurface);

            return new RenderSession(_parent, new CompositeDisposable(contextToken, eglSurface));
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Cleanup per-buffer resources
        foreach (var (fd, resources) in _dmaBufferResources)
        {
            var gl = _deferredContext.GlInterface;
            gl.DeleteFramebuffer(resources.Framebuffer);
            gl.DeleteRenderbuffer(resources.Renderbuffer);
            _eglDestroyImageKHR?.Invoke(_eglDisplay.Handle, resources.EglImage);
        }

        _dmaBufferResources.Clear();

        _deferredContext?.Dispose();
        _eglDisplay?.Dispose();
        _gbmDummySurface?.Dispose();
        _gbmDevice?.Dispose();
    }

    private class DmaBufferGlResources
    {
        public required IntPtr EglImage { get; init; }
        public required int Renderbuffer { get; init; }
        public required int Framebuffer { get; init; }
    }

    private class CompositeDisposable : IDisposable
    {
        private readonly IDisposable[] _disposables;

        public CompositeDisposable(params IDisposable[] disposables)
        {
            _disposables = disposables;
        }

        public void Dispose()
        {
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }
        }
    }
}