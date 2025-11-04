using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Native libudev bindings for device enumeration.
/// </summary>
[SupportedOSPlatform("linux")]
public static class LibUdev
{
 private const string LibraryName = "libudev.so.1";

    /// <summary>
    /// Create a new udev context.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "udev_new")]
    public static extern nint udev_new();

    /// <summary>
    /// Decrease reference count and free resources if needed.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "udev_unref")]
    public static extern nint udev_unref(nint udev);

    /// <summary>
    /// Increase reference count.
 /// </summary>
    [DllImport(LibraryName, EntryPoint = "udev_ref")]
    public static extern nint udev_ref(nint udev);
}
