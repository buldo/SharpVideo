using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SharpVideo.Linux.Native.C;

namespace SharpVideo.Linux.Native;

[SupportedOSPlatform("linux")]
public static partial class Libc
{
    private const string LibraryName = "libc";


    /// <summary>
    /// Opens the file specified by pathname.
    /// </summary>
    /// <param name="pathname">The path to the file.</param>
    /// <param name="flags">The file access mode and other options.</param>
    /// <returns>A file descriptor on success, or -1 on error.</returns>
    [LibraryImport(
        LibraryName,
        EntryPoint = "open",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf8)]
    public static partial int open(string pathname, OpenFlags flags);

    /// <summary>
    /// Opens the file specified by pathname. If O_CREAT is in flags, the file is created with the specified mode.
    /// </summary>
    /// <param name="pathname">The path to the file.</param>
    /// <param name="flags">The file access mode and other options.</param>
    /// <param name="mode">The file permissions to use when creating the file.</param>
    /// <returns>A file descriptor on success, or -1 on error.</returns>
    [LibraryImport(
        LibraryName,
        EntryPoint = "open",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf8)]
    public static partial int open(string pathname, OpenFlags flags, int mode);

    /// <summary>
    /// Closes a file descriptor.
    /// </summary>
    /// <param name="fd">The file descriptor to close.</param>
    /// <returns>0 on success, or -1 on error.</returns>
    [LibraryImport(
        LibraryName,
        EntryPoint = "close",
        SetLastError = true)]
    public static partial int close(int fd);

    /// <summary>
    /// Performs device-specific input/output operations on a file descriptor.
    /// </summary>
    /// <param name="fd">The file descriptor.</param>
    /// <param name="request">The device-specific request code.</param>
    /// <param name="argp">Pointer to the argument data.</param>
    /// <returns>0 on success, or -1 on error.</returns>
    [LibraryImport(
        LibraryName,
        EntryPoint = "ioctl",
        SetLastError = true)]
    public static partial int ioctl(int fd, uint request, nint argp);

    /// <summary>
    /// Performs device-specific input/output operations on a file descriptor.
    /// </summary>
    /// <param name="fd">The file descriptor.</param>
    /// <param name="request">The device-specific request code.</param>
    /// <param name="argp">Pointer to the argument data.</param>
    /// <returns>0 on success, or -1 on error.</returns>
    [LibraryImport(
        LibraryName,
        EntryPoint = "ioctl",
        SetLastError = true)]
    public static partial int ioctl(int fd, ulong request, nint argp);

    public static readonly IntPtr MAP_FAILED = new IntPtr(-1);

    /// <summary>
    /// Map addresses starting near ADDR and extending for LEN bytes.
    /// From OFFSET into the file FD describes according to PROT and FLAGS.
    /// If ADDR is nonzero, it is the desired mapping address.
    /// If the MAP_FIXED bit is set in FLAGS, the mapping will be at ADDR exactly (which must be page-aligned); otherwise the system chooses a convenient nearby address.
    /// The return value is the actual mapping address chosen or MAP_FAILED for errors (in which case `errno' is set).
    /// A successful `mmap' call deallocates any previous mapping for the affected region.
    /// </summary>
    /// <remarks>
    /// extern void *mmap (void *__addr, size_t __len, int __prot,int __flags, int __fd, __off_t __offset) __THROW;
    /// </remarks>
    /// <param name="addr">The starting address for the mapping (usually IntPtr.Zero).</param>
    /// <param name="length">The length of the mapping.</param>
    /// <param name="prot">The desired memory protection of the mapping.</param>
    /// <param name="flags">The type of mapping.</param>
    /// <param name="fd">The file descriptor of the file to be mapped.</param>
    /// <param name="offset">The offset within the file where the mapping should start.</param>
    /// <returns>
    /// On success, returns the starting address of the mapped area.
    /// On error, returns MAP_FAILED and sets errno appropriately.
    /// </returns>
    [LibraryImport(
        LibraryName,
        EntryPoint = "mmap",
        SetLastError = true)]
    public static partial IntPtr mmap(IntPtr addr, nuint length, ProtFlags prot, MapFlags flags, int fd, nint offset);

    /// <summary>
    /// Deallocate any mapping for the region starting at ADDR and extending LEN bytes.
    /// Returns 0 if successful, -1 for errors (and sets errno).
    /// </summary>
    /// <remarks>
    /// extern int munmap (void *__addr, size_t __len) __THROW;
    /// </remarks>
    /// <param name="addr">The starting address of the mapping to be removed.</param>
    /// <param name="length">The length of the mapping to be removed.</param>
    /// <returns>0 on success, or -1 on error.</returns>
    [LibraryImport(
        LibraryName,
        EntryPoint = "munmap",
        SetLastError = true)]
    public static unsafe partial int munmap(void* addr, nuint length);

    /// <summary>
    /// Synchronize the region starting at ADDR and extending LEN bytes with the file it maps.
    /// Filesystem operations on a file being mapped are unpredictable before this is done.
    /// Flags are from the MS_* set.
    /// This function is a cancellation point and therefore not marked with __THROW.
    /// </summary>
    /// <remarks>
    /// extern int msync (void *__addr, size_t __len, int __flags);
    /// </remarks>
    /// <param name="addr">The starting address of the mapping to synchronize.</param>
    /// <param name="length">The length of the mapping to synchronize.</param>
    /// <param name="flags">Synchronization flags.</param>
    /// <returns>0 on success, or -1 on error.</returns>
    [LibraryImport(
        LibraryName,
        EntryPoint = "msync",
        SetLastError = true)]
    public static unsafe partial int msync(void* addr, nuint length, MsyncFlags flags);

    /// <summary>
    /// Changes the memory protection of a mapped region.
    /// </summary>
    /// <param name="addr">The starting address of the mapping to protect.</param>
    /// <param name="length">The length of the mapping to protect.</param>
    /// <param name="prot">The new protection flags.</param>
    /// <returns>0 on success, or -1 on error.</returns>
    [LibraryImport(
        LibraryName,
        EntryPoint = "mprotect",
        SetLastError = true)]
    public static unsafe partial int mprotect(void* addr, nuint length, ProtFlags prot);

    /// <summary>
    /// Wait for some event on a file descriptor.
    /// </summary>
    /// <param name="fds">Array of pollfd structures.</param>
    /// <param name="nfds">Number of file descriptors.</param>
    /// <param name="timeout">Timeout in milliseconds (-1 for infinite).</param>
    /// <returns>Number of file descriptors with events, 0 on timeout, -1 on error.</returns>
    [LibraryImport(
        LibraryName,
        EntryPoint = "poll",
        SetLastError = true)]
    public static unsafe partial int poll(ref PollFd fds, nuint nfds, int timeout);
}