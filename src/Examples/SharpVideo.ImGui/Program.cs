using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImGui.Backends.SDL3;
using Hexa.NET.OpenGL;
using Hexa.NET.SDL3;

using SDLEvent = Hexa.NET.SDL3.SDLEvent;
using SDLWindow = Hexa.NET.SDL3.SDLWindow;

namespace SharpVideo.ImGui;

internal class Program
{
    static void Main(string[] args)
    {
        SDL.SetHint(SDL.SDL_HINT_MOUSE_FOCUS_CLICKTHROUGH, "1");
        SDL.Init(SDLInitFlags.Events | SDLInitFlags.Video);
        unsafe
        {
            float main_scale = SDL.GetDisplayContentScale(SDL.GetPrimaryDisplay());
            var window = SDL.CreateWindow("Test Window", (int)(1280 * main_scale), (int)(720 * main_scale), SDLWindowFlags.Resizable | SDLWindowFlags.Opengl | SDLWindowFlags.HighPixelDensity);
            var windowId = SDL.GetWindowID(window);

            var guiContext = Hexa.NET.ImGui.ImGui.CreateContext();
            Hexa.NET.ImGui.ImGui.SetCurrentContext(guiContext);

            // Setup ImGui config.
            var io = Hexa.NET.ImGui.ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;     // Enable Keyboard Controls
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableGamepad;      // Enable Gamepad Controls
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;         // Enable Docking
            io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;       // Enable Multi-Viewport / Platform Windows
            io.ConfigViewportsNoAutoMerge = false;
            io.ConfigViewportsNoTaskBarIcon = false;

            var style = Hexa.NET.ImGui.ImGui.GetStyle();
            style.ScaleAllSizes(main_scale);        // Bake a fixed style scale. (until we have a solution for dynamic style scaling, changing this requires resetting Style + calling this again)
            style.FontScaleDpi = main_scale;        // Set initial font scale. (using io.ConfigDpiScaleFonts=true makes this unnecessary. We leave both here for documentation purpose)
            io.ConfigDpiScaleFonts = true;          // [Experimental] Automatically overwrite style.FontScaleDpi in Begin() when Monitor DPI changes. This will scale fonts but _NOT_ scale sizes/padding for now.
            io.ConfigDpiScaleViewports = true;

            var context = SDL.GLCreateContext(window);

            ImGuiImplSDL3.SetCurrentContext(guiContext);
            if (!ImGuiImplSDL3.InitForOpenGL(new SDLWindowPtr((Hexa.NET.ImGui.Backends.SDL3.SDLWindow*)window), (void*)context.Handle))
            {
                Console.WriteLine("Failed to init ImGui Impl SDL3");
                SDL.Quit();
                return;
            }

            ImGuiImplOpenGL3.SetCurrentContext(guiContext);
            if (!ImGuiImplOpenGL3.Init((byte*)null))
            {
                Console.WriteLine("Failed to init ImGui Impl OpenGL3");
                SDL.Quit();
                return;
            }

            GL GL = new(new BindingsContext(window, context));

            SDLEvent sdlEvent = default;
            bool exiting = false;
            while (!exiting)
            {
                SDL.PumpEvents();

                while (SDL.PollEvent(ref sdlEvent))
                {
                    ImGuiImplSDL3.ProcessEvent((Hexa.NET.ImGui.Backends.SDL3.SDLEvent*)&sdlEvent);

                    switch ((SDLEventType)sdlEvent.Type)
                    {
                        case SDLEventType.Quit:
                            exiting = true;
                            break;

                        case SDLEventType.Terminating:
                            exiting = true;
                            break;

                        case SDLEventType.WindowCloseRequested:
                            var windowEvent = sdlEvent.Window;
                            if (windowEvent.WindowID == windowId)
                            {
                                exiting = true;
                            }
                            break;
                    }
                }

                GL.MakeCurrent();
                GL.ClearColor(1, 0.8f, 0.75f, 1);
                GL.Clear(GLClearBufferMask.ColorBufferBit);

                ImGuiImplOpenGL3.NewFrame();
                ImGuiImplSDL3.NewFrame();
                Hexa.NET.ImGui.ImGui.NewFrame();

                Hexa.NET.ImGui.ImGui.ShowDemoWindow();

                Hexa.NET.ImGui.ImGui.Render();
                Hexa.NET.ImGui.ImGui.EndFrame();

                GL.MakeCurrent();
                ImGuiImplOpenGL3.RenderDrawData(Hexa.NET.ImGui.ImGui.GetDrawData());

                if ((io.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
                {
                    Hexa.NET.ImGui.ImGui.UpdatePlatformWindows();
                    Hexa.NET.ImGui.ImGui.RenderPlatformWindowsDefault();
                }

                GL.MakeCurrent();

                // Swap front and back buffers (double buffering)
                GL.SwapBuffers();
            }

            ImGuiImplOpenGL3.Shutdown();
            ImGuiImplSDL3.Shutdown();
            Hexa.NET.ImGui.ImGui.DestroyContext();
            GL.Dispose();

            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }
}


internal unsafe class BindingsContext : HexaGen.Runtime.IGLContext
{
    private readonly SDLWindow* window;
    private readonly SDLGLContext context;

    public BindingsContext(SDLWindow* window, SDLGLContext context)
    {
        this.window = window;
        this.context = context;
    }

    public nint Handle => (nint)window;

    public bool IsCurrent => SDL.GLGetCurrentContext() == context;

    public void Dispose()
    {
    }

    public nint GetProcAddress(string procName)
    {
        return (nint)SDL.GLGetProcAddress(procName);
    }

    public bool IsExtensionSupported(string extensionName)
    {
        return SDL.GLExtensionSupported(extensionName);
    }

    public void MakeCurrent()
    {
        SDL.GLMakeCurrent(window, context);
    }

    public void SwapBuffers()
    {
        SDL.GLSwapWindow(window);
    }

    public void SwapInterval(int interval)
    {
        SDL.GLSetSwapInterval(interval);
    }

    public bool TryGetProcAddress(string procName, out nint procAddress)
    {
        procAddress = (nint)SDL.GLGetProcAddress(procName);
        return procAddress != 0;
    }
}