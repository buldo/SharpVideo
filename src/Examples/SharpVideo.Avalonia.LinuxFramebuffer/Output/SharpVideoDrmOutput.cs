using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.LinuxFramebuffer.Output;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Egl;
using Avalonia.OpenGL.Surfaces;
using Avalonia.Platform;
using SharpVideo.Drm;
using SharpVideo.Gbm;
using SharpVideo.Linux.Native.Gbm;
using SharpVideo.Utils;
using static SharpVideo.Avalonia.LinuxFramebuffer.NativeUnsafeMethods;
using static SharpVideo.Avalonia.LinuxFramebuffer.Output.LibDrm;

namespace SharpVideo.Avalonia.LinuxFramebuffer.Output;

public unsafe class SharpVideoDrmOutput : IGlOutputBackend, IGlPlatformSurface
{
    private readonly DrmDevice _drmDevice;
    private readonly DrmPlaneDoubleBufferPresenter _presenter;
    private GbmSurface _gbmTargetSurface;
    private EglDisplay _eglDisplay;
    private EglSurface _eglSurface;
    private EglContext _deferredContext;


    [DllImport("libEGL.so.1")]
    static extern IntPtr eglGetProcAddress(string proc);

    public SharpVideoDrmOutput(
        DrmDevice drmDevice,
        DrmPlaneDoubleBufferPresenter presenter)
    {
        _drmDevice = drmDevice;
        _presenter = presenter;
        Init();
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

    uint GetFbIdForBo(IntPtr bo)
    {
        if (bo == IntPtr.Zero)
            throw new ArgumentException("bo is 0");
        var data = gbm_bo_get_user_data(bo);
        if (data != IntPtr.Zero)
            return (uint)data.ToInt32();

        var w = gbm_bo_get_width(bo);
        var h = gbm_bo_get_height(bo);
        var stride = gbm_bo_get_stride(bo);
        var handle = gbm_bo_get_handle(bo).u32;
        var format = gbm_bo_get_format(bo);

        // prepare for the new ioctl call
        var handles = new uint[] { handle, 0, 0, 0 };
        var pitches = new uint[] { stride, 0, 0, 0 };
        var offsets = new uint[4];

        var ret = drmModeAddFB2(_card.Fd, w, h, format, handles, pitches,
            offsets, out var fbHandle, 0);

        if (ret != 0)
        {
            // legacy fallback
            ret = drmModeAddFB(_card.Fd, w, h, 24, 32, stride, (uint)handle,
                out fbHandle);

            if (ret != 0)
                throw new Win32Exception(ret, $"drmModeAddFb failed {ret}");
        }

        gbm_bo_set_user_data(bo, new IntPtr((int)fbHandle), FbDestroyDelegate);


        return fbHandle;
    }

    void Init()
    {
        var device = GbmDevice.CreateFromDrmDevice(_drmDevice);
        _gbmTargetSurface = device.CreateSurface(
            _presenter.Width, _presenter.Height,
            KnownPixelFormats.DRM_FORMAT_ARGB8888,
            GbmBoUse.GBM_BO_USE_SCANOUT | GbmBoUse.GBM_BO_USE_RENDERING);


        _eglDisplay = new EglDisplay(
            new EglDisplayCreationOptions
            {
                Egl = new EglInterface(eglGetProcAddress),
                PlatformType = 0x31D7,
                PlatformDisplay = device.Fd,
                SupportsMultipleContexts = true,
                SupportsContextSharing = true
            });

        var surface = _eglDisplay.EglInterface.CreateWindowSurface(_eglDisplay.Handle, _eglDisplay.Config, _gbmTargetSurface.Fd, new[] { EglConsts.EGL_NONE, EglConsts.EGL_NONE });

        _eglSurface = new EglSurface(_eglDisplay, surface);

        _deferredContext = _eglDisplay.CreateContext(null);
        PlatformGraphics = new SharedContextGraphics(_deferredContext);

        var initialBufferSwappingColorR = 0 / 255.0f;
        var initialBufferSwappingColorG = 0 / 255.0f;
        var initialBufferSwappingColorB = 0 / 255.0f;
        var initialBufferSwappingColorA = 0 / 255.0f;
        using (_deferredContext.MakeCurrent(_eglSurface))
        {
            _deferredContext.GlInterface.ClearColor(
                initialBufferSwappingColorR,
                initialBufferSwappingColorG,
                initialBufferSwappingColorB,
                initialBufferSwappingColorA);
            _deferredContext.GlInterface.Clear(GlConsts.GL_COLOR_BUFFER_BIT | GlConsts.GL_STENCIL_BUFFER_BIT);
            _eglSurface.SwapBuffers();
        }

        var bo = gbm_surface_lock_front_buffer(_gbmTargetSurface.Fd);
        //var fbId = GetFbIdForBo(bo);
        //var connectorId = connector.Id;
        //var mode = modeInfo.Mode;


        //var res = drmModeSetCrtc(_card.Fd, _crtcId, fbId, 0, 0, &connectorId, 1, &mode);
        //if (res != 0)
        //    throw new Win32Exception(res, "drmModeSetCrtc failed");

        //_mode = mode;
        //_currentBo = bo;

        //if (_outputOptions.EnableInitialBufferSwapping)
        //{
        //    //Go through two cycles of buffer swapping (there are render artifacts otherwise)
        //    for (var c = 0; c < 2; c++)
        //        using (CreateGlRenderTarget().BeginDraw())
        //        {
        //            _deferredContext.GlInterface.ClearColor(initialBufferSwappingColorR, initialBufferSwappingColorG,
        //                initialBufferSwappingColorB, initialBufferSwappingColorA);
        //            _deferredContext.GlInterface.Clear(GlConsts.GL_COLOR_BUFFER_BIT | GlConsts.GL_STENCIL_BUFFER_BIT);
        //        }
        //}

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
            // We are wrapping GBM buffer chain associated with CRTC, and don't free it on a whim
        }

        class RenderSession : IGlPlatformSurfaceRenderingSession
        {
            private readonly SharpVideoDrmOutput _parent;
            private readonly IDisposable _clearContext;

            public RenderSession(SharpVideoDrmOutput parent, IDisposable clearContext)
            {
                _parent = parent;
                _clearContext = clearContext;
            }

            public void Dispose()
            {
                _parent._deferredContext.GlInterface.Flush();
                _parent._eglSurface.SwapBuffers();

                var nextBo = gbm_surface_lock_front_buffer(_parent._gbmTargetSurface);
                if (nextBo == IntPtr.Zero)
                {
                    // Not sure what else can be done
                    Console.WriteLine("gbm_surface_lock_front_buffer failed");
                }
                else
                {

                    var fb = _parent.GetFbIdForBo(nextBo);
                    bool waitingForFlip = true;

                    drmModePageFlip(_parent._card.Fd, _parent._crtcId, fb, DrmModePageFlip.Event, null);

                    DrmEventPageFlipHandlerDelegate flipCb =
                        (int fd, uint sequence, uint tv_sec, uint tv_usec, void* user_data) =>
                        {
                            waitingForFlip = false;
                        };
                    var cbHandle = GCHandle.Alloc(flipCb);
                    var ctx = new DrmEventContext
                    {
                        version = 4,
                        page_flip_handler = Marshal.GetFunctionPointerForDelegate(flipCb)
                    };
                    while (waitingForFlip)
                    {
                        var pfd = new PollFd {events = 1, fd = _parent._card.Fd};
                        poll(&pfd, new IntPtr(1), -1);
                        drmHandleEvent(_parent._card.Fd, &ctx);
                    }

                    cbHandle.Free();
                    gbm_surface_release_buffer(_parent._gbmTargetSurface, _parent._currentBo);
                    _parent._currentBo = nextBo;
                }
                _clearContext.Dispose();
            }


            public IGlContext Context => _parent._deferredContext;

            public PixelSize Size => _parent._mode.Resolution;

            public double Scaling => _parent.Scaling;

            public bool IsYFlipped => false;
        }

        public IGlPlatformSurfaceRenderingSession BeginDraw()
        {
            return new RenderSession(_parent, _parent._deferredContext.MakeCurrent(_parent._eglSurface));
        }
    }
}