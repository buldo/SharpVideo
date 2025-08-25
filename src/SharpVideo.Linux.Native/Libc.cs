using System.Runtime.InteropServices;
using System.Runtime.Versioning;

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

    [Flags]
    public enum ProtFlags
    {
        PROT_NONE = 0x0,
        PROT_READ = 0x1,
        PROT_WRITE = 0x2,
        PROT_EXEC = 0x4
    }

    [Flags]
    public enum MapFlags
    {
        MAP_SHARED = 0x01,
        MAP_PRIVATE = 0x02,
        MAP_FIXED = 0x10
    }

    public static readonly IntPtr MAP_FAILED = new IntPtr(-1);

    /// <summary>
    /// Maps files or devices into memory; it is used to map the file descriptor `fd` into the address space of the calling process.
    /// </summary>
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
    public static partial IntPtr mmap(IntPtr addr, IntPtr length, ProtFlags prot, MapFlags flags, int fd, IntPtr offset);

    /// <summary>
    /// Unmaps a mapped region of memory, reverting it back to being unallocated.
    /// </summary>
    /// <param name="addr">The starting address of the mapping to be removed.</param>
    /// <param name="length">The length of the mapping to be removed.</param>
    /// <returns>0 on success, or -1 on error.</returns>
    [LibraryImport(
        LibraryName,
        EntryPoint = "munmap",
        SetLastError = true)]
    public static partial int munmap(IntPtr addr, IntPtr length);
}