using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Native libinput bindings for input device handling.
/// </summary>
[SupportedOSPlatform("linux")]
public static unsafe class LibInput
{
    private const string LibraryName = "libinput.so.10";

    // Event types
    public const int LIBINPUT_EVENT_NONE = 0;
    public const int LIBINPUT_EVENT_DEVICE_ADDED = 1;
    public const int LIBINPUT_EVENT_DEVICE_REMOVED = 2;
    public const int LIBINPUT_EVENT_KEYBOARD_KEY = 300;
    public const int LIBINPUT_EVENT_POINTER_MOTION = 400;
    public const int LIBINPUT_EVENT_POINTER_MOTION_ABSOLUTE = 401;
    public const int LIBINPUT_EVENT_POINTER_BUTTON = 402;
    public const int LIBINPUT_EVENT_POINTER_AXIS = 403;
    public const int LIBINPUT_EVENT_POINTER_SCROLL_WHEEL = 404;
    public const int LIBINPUT_EVENT_POINTER_SCROLL_FINGER = 405;
    public const int LIBINPUT_EVENT_POINTER_SCROLL_CONTINUOUS = 406;
    public const int LIBINPUT_EVENT_TOUCH_DOWN = 500;
    public const int LIBINPUT_EVENT_TOUCH_UP = 501;
    public const int LIBINPUT_EVENT_TOUCH_MOTION = 502;
    public const int LIBINPUT_EVENT_TOUCH_CANCEL = 503;
    public const int LIBINPUT_EVENT_TOUCH_FRAME = 504;

    // Button states
    public const int LIBINPUT_BUTTON_STATE_RELEASED = 0;
    public const int LIBINPUT_BUTTON_STATE_PRESSED = 1;

    // Key states
    public const int LIBINPUT_KEY_STATE_RELEASED = 0;
    public const int LIBINPUT_KEY_STATE_PRESSED = 1;

    // Mouse buttons (Linux input event codes)
    public const uint BTN_LEFT = 0x110;
  public const uint BTN_RIGHT = 0x111;
    public const uint BTN_MIDDLE = 0x112;
    public const uint BTN_SIDE = 0x113;
    public const uint BTN_EXTRA = 0x114;

    /// <summary>
    /// Create a new libinput context from udev.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_udev_create_context")]
    public static extern nint udev_create_context(
        nint interface_ptr,
        nint user_data,
        nint udev);

    /// <summary>
    /// Assign a seat to this libinput context.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_udev_assign_seat")]
    public static extern int udev_assign_seat(nint libinput, [MarshalAs(UnmanagedType.LPStr)] string seat_id);

    /// <summary>
  /// Get the file descriptor for the libinput context.
 /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_get_fd")]
    public static extern int get_fd(nint libinput);

    /// <summary>
    /// Dispatch events from the file descriptor.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_dispatch")]
    public static extern int dispatch(nint libinput);

    /// <summary>
    /// Get the next event from the internal event queue.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_get_event")]
    public static extern nint get_event(nint libinput);

    /// <summary>
    /// Get the event type.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_event_get_type")]
  public static extern int event_get_type(nint event_ptr);

    /// <summary>
    /// Get the device associated with this event.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_event_get_device")]
    public static extern nint event_get_device(nint event_ptr);

    /// <summary>
    /// Destroy an event.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_event_destroy")]
    public static extern void event_destroy(nint event_ptr);

 /// <summary>
  /// Get pointer event from generic event.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_event_get_pointer_event")]
    public static extern nint event_get_pointer_event(nint event_ptr);

    /// <summary>
    /// Get keyboard event from generic event.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_event_get_keyboard_event")]
 public static extern nint event_get_keyboard_event(nint event_ptr);

/// <summary>
    /// Get the delta x for a pointer motion event.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_event_pointer_get_dx")]
    public static extern double pointer_get_dx(nint pointer_event);

    /// <summary>
    /// Get the delta y for a pointer motion event.
  /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_event_pointer_get_dy")]
  public static extern double pointer_get_dy(nint pointer_event);

    /// <summary>
    /// Get the absolute x coordinate for a pointer event.
    /// </summary>
  [DllImport(LibraryName, EntryPoint = "libinput_event_pointer_get_absolute_x")]
    public static extern double pointer_get_absolute_x(nint pointer_event);

    /// <summary>
    /// Get the absolute y coordinate for a pointer event.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_event_pointer_get_absolute_y")]
  public static extern double pointer_get_absolute_y(nint pointer_event);

    /// <summary>
    /// Transform absolute x coordinate to screen coordinate.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_event_pointer_get_absolute_x_transformed")]
    public static extern double pointer_get_absolute_x_transformed(nint pointer_event, uint width);

    /// <summary>
    /// Transform absolute y coordinate to screen coordinate.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_event_pointer_get_absolute_y_transformed")]
    public static extern double pointer_get_absolute_y_transformed(nint pointer_event, uint height);

    /// <summary>
    /// Get the button that triggered this event.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_event_pointer_get_button")]
    public static extern uint pointer_get_button(nint pointer_event);

    /// <summary>
    /// Get the button state.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_event_pointer_get_button_state")]
    public static extern int pointer_get_button_state(nint pointer_event);

    /// <summary>
    /// Get the axis value for scroll events.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_event_pointer_get_axis_value")]
    public static extern double pointer_get_axis_value(nint pointer_event, int axis);

    /// <summary>
 /// Get the key code for a keyboard event.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_event_keyboard_get_key")]
    public static extern uint keyboard_get_key(nint keyboard_event);

    /// <summary>
    /// Get the key state for a keyboard event.
    /// </summary>
 [DllImport(LibraryName, EntryPoint = "libinput_event_keyboard_get_key_state")]
    public static extern int keyboard_get_key_state(nint keyboard_event);

 /// <summary>
    /// Increase the refcount of the context.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_ref")]
    public static extern nint @ref(nint libinput);

    /// <summary>
    /// Decrease the refcount of the context.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_unref")]
    public static extern nint unref(nint libinput);

    /// <summary>
    /// Suspend monitoring for new devices.
 /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_suspend")]
    public static extern void suspend(nint libinput);

    /// <summary>
    /// Resume monitoring for new devices.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "libinput_resume")]
    public static extern int resume(nint libinput);

    // Axis types
    public const int LIBINPUT_POINTER_AXIS_SCROLL_VERTICAL = 0;
    public const int LIBINPUT_POINTER_AXIS_SCROLL_HORIZONTAL = 1;

    /// <summary>
    /// Interface callbacks for libinput.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct libinput_interface
    {
  public nint open_restricted;
        public nint close_restricted;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int OpenRestrictedCallback(
        [MarshalAs(UnmanagedType.LPStr)] string path,
        int flags,
  nint user_data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void CloseRestrictedCallback(int fd, nint user_data);
}
