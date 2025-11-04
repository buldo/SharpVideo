using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Linux.Native;
using SharpVideo.Linux.Native.C;

namespace SharpVideo.Utils;

/// <summary>
/// Manages input devices using libinput for mouse, keyboard, and gamepad support.
/// Thread-safe input event processor for DRM/KMS applications.
/// </summary>
[SupportedOSPlatform("linux")]
public class InputManager : IDisposable
{
    private readonly ILogger _logger;
    private nint _udev;
    private nint _libinput;
    private readonly GCHandle _interfaceHandle;
    private readonly GCHandle _openCallbackHandle;
    private readonly GCHandle _closeCallbackHandle;
    private readonly LibInput.libinput_interface _interface;
    private bool _disposed;

    // Input state
    private float _mouseX;
private float _mouseY;
    private readonly bool[] _mouseButtons = new bool[8];
    private readonly HashSet<uint> _pressedKeys = new();

    // Screen dimensions for absolute positioning
    private readonly uint _screenWidth;
 private readonly uint _screenHeight;

    public InputManager(uint screenWidth, uint screenHeight, ILogger logger)
    {
      _screenWidth = screenWidth;
        _screenHeight = screenHeight;
        _logger = logger;

        _logger.LogInformation("Initializing libinput for input device handling...");

 // Create udev context
        _udev = LibUdev.udev_new();
        if (_udev == 0)
        {
            throw new Exception("Failed to create udev context");
        }

        _logger.LogDebug("Created udev context");

        // Setup libinput interface callbacks
        var openCallback = new LibInput.OpenRestrictedCallback(OpenRestricted);
        var closeCallback = new LibInput.CloseRestrictedCallback(CloseRestricted);

        _openCallbackHandle = GCHandle.Alloc(openCallback);
  _closeCallbackHandle = GCHandle.Alloc(closeCallback);

        _interface = new LibInput.libinput_interface
        {
            open_restricted = Marshal.GetFunctionPointerForDelegate(openCallback),
            close_restricted = Marshal.GetFunctionPointerForDelegate(closeCallback)
  };

        _interfaceHandle = GCHandle.Alloc(_interface, GCHandleType.Pinned);

      // Create libinput context
    _libinput = LibInput.udev_create_context(
       _interfaceHandle.AddrOfPinnedObject(),
        0,
    _udev);

        if (_libinput == 0)
 {
       throw new Exception("Failed to create libinput context");
     }

     _logger.LogDebug("Created libinput context");

        // Assign default seat "seat0" (all input devices)
        var result = LibInput.udev_assign_seat(_libinput, "seat0");
if (result != 0)
        {
            throw new Exception($"Failed to assign seat to libinput context: {result}");
        }

        _logger.LogInformation("Assigned seat0 to libinput - monitoring all input devices");
    }

    /// <summary>
    /// Gets the file descriptor for polling input events.
/// </summary>
    public int GetFileDescriptor()
    {
        return LibInput.get_fd(_libinput);
    }

  /// <summary>
    /// Gets current mouse position.
    /// </summary>
    public (float x, float y) GetMousePosition() => (_mouseX, _mouseY);

    /// <summary>
  /// Gets mouse button state (0=left, 1=right, 2=middle).
 /// </summary>
    public bool IsMouseButtonDown(int button)
    {
return button >= 0 && button < _mouseButtons.Length && _mouseButtons[button];
    }

    /// <summary>
    /// Checks if a key is currently pressed.
    /// </summary>
    public bool IsKeyDown(uint keyCode)
    {
        return _pressedKeys.Contains(keyCode);
    }

    /// <summary>
    /// Process input events. Call this when the file descriptor is ready.
    /// Returns true if there were events to process.
    /// </summary>
    public bool ProcessEvents()
    {
        // Dispatch events from file descriptor
        var result = LibInput.dispatch(_libinput);
        if (result != 0)
        {
        _logger.LogWarning("libinput dispatch failed: {Result}", result);
            return false;
        }

 bool hadEvents = false;

        // Process all available events
      nint eventPtr;
        while ((eventPtr = LibInput.get_event(_libinput)) != 0)
      {
         hadEvents = true;
   ProcessEvent(eventPtr);
            LibInput.event_destroy(eventPtr);
        }

  return hadEvents;
    }

    private void ProcessEvent(nint eventPtr)
    {
   var eventType = LibInput.event_get_type(eventPtr);

    switch (eventType)
        {
            case LibInput.LIBINPUT_EVENT_DEVICE_ADDED:
        {
          var device = LibInput.event_get_device(eventPtr);
        _logger.LogDebug("Input device added: 0x{Device:X}", device);
   break;
          }

        case LibInput.LIBINPUT_EVENT_DEVICE_REMOVED:
             {
              var device = LibInput.event_get_device(eventPtr);
            _logger.LogDebug("Input device removed: 0x{Device:X}", device);
    break;
        }

            case LibInput.LIBINPUT_EVENT_POINTER_MOTION:
   {
            var pointerEvent = LibInput.event_get_pointer_event(eventPtr);
    var dx = (float)LibInput.pointer_get_dx(pointerEvent);
          var dy = (float)LibInput.pointer_get_dy(pointerEvent);

              _mouseX = Math.Clamp(_mouseX + dx, 0, _screenWidth);
     _mouseY = Math.Clamp(_mouseY + dy, 0, _screenHeight);
     break;
    }

            case LibInput.LIBINPUT_EVENT_POINTER_MOTION_ABSOLUTE:
                {
             var pointerEvent = LibInput.event_get_pointer_event(eventPtr);
        _mouseX = (float)LibInput.pointer_get_absolute_x_transformed(pointerEvent, _screenWidth);
         _mouseY = (float)LibInput.pointer_get_absolute_y_transformed(pointerEvent, _screenHeight);
          break;
       }

      case LibInput.LIBINPUT_EVENT_POINTER_BUTTON:
      {
            var pointerEvent = LibInput.event_get_pointer_event(eventPtr);
         var button = LibInput.pointer_get_button(pointerEvent);
     var state = LibInput.pointer_get_button_state(pointerEvent);

     // Map Linux button codes to indices
    int buttonIndex = button switch
           {
         LibInput.BTN_LEFT => 0,
         LibInput.BTN_RIGHT => 1,
      LibInput.BTN_MIDDLE => 2,
      LibInput.BTN_SIDE => 3,
     LibInput.BTN_EXTRA => 4,
           _ => -1
            };

   if (buttonIndex >= 0 && buttonIndex < _mouseButtons.Length)
         {
       _mouseButtons[buttonIndex] = state == LibInput.LIBINPUT_BUTTON_STATE_PRESSED;
}
     break;
       }

     case LibInput.LIBINPUT_EVENT_POINTER_AXIS:
        case LibInput.LIBINPUT_EVENT_POINTER_SCROLL_WHEEL:
     {
     var pointerEvent = LibInput.event_get_pointer_event(eventPtr);
      var verticalScroll = LibInput.pointer_get_axis_value(
              pointerEvent,
          LibInput.LIBINPUT_POINTER_AXIS_SCROLL_VERTICAL);
        var horizontalScroll = LibInput.pointer_get_axis_value(
         pointerEvent,
      LibInput.LIBINPUT_POINTER_AXIS_SCROLL_HORIZONTAL);

        // TODO: Expose scroll events via callback or event queue
    _logger.LogTrace("Scroll: V={Vertical}, H={Horizontal}", verticalScroll, horizontalScroll);
        break;
   }

 case LibInput.LIBINPUT_EVENT_KEYBOARD_KEY:
    {
       var keyboardEvent = LibInput.event_get_keyboard_event(eventPtr);
         var key = LibInput.keyboard_get_key(keyboardEvent);
        var state = LibInput.keyboard_get_key_state(keyboardEvent);

 if (state == LibInput.LIBINPUT_KEY_STATE_PRESSED)
  {
         _pressedKeys.Add(key);
  }
        else
        {
    _pressedKeys.Remove(key);
       }

      _logger.LogTrace("Key {Key} {State}", key, state == LibInput.LIBINPUT_KEY_STATE_PRESSED ? "pressed" : "released");
 break;
              }
        }
    }

    private int OpenRestricted(string path, int flags, nint userData)
    {
        _logger.LogDebug("Opening input device: {Path}", path);
        var fd = Libc.open(path, (OpenFlags)flags);
        if (fd < 0)
        {
   var errno = Marshal.GetLastPInvokeError();
      _logger.LogWarning("Failed to open {Path}: errno {Errno}", path, errno);
     }
        return fd;
    }

    private void CloseRestricted(int fd, nint userData)
    {
  _logger.LogDebug("Closing input device fd: {Fd}", fd);
        Libc.close(fd);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogInformation("Disposing InputManager");

        if (_libinput != 0)
        {
   LibInput.unref(_libinput);
   _libinput = 0;
        }

        if (_udev != 0)
        {
       LibUdev.udev_unref(_udev);
            _udev = 0;
        }

        if (_interfaceHandle.IsAllocated)
            _interfaceHandle.Free();

        if (_openCallbackHandle.IsAllocated)
       _openCallbackHandle.Free();

     if (_closeCallbackHandle.IsAllocated)
            _closeCallbackHandle.Free();

   _disposed = true;
        _logger.LogInformation("InputManager disposed");
    }
}
