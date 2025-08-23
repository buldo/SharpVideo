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
    [LibraryImport(LibraryName, EntryPoint = "open", SetLastError = true)]
    public static partial int open(string pathname, OpenFlags flags);

    /// <summary>
    /// Opens the file specified by pathname. If O_CREAT is in flags, the file is created with the specified mode.
    /// </summary>
    /// <param name="pathname">The path to the file.</param>
    /// <param name="flags">The file access mode and other options.</param>
    /// <param name="mode">The file permissions to use when creating the file.</param>
    /// <returns>A file descriptor on success, or -1 on error.</returns>
    [LibraryImport(LibraryName, EntryPoint = "open", SetLastError = true)]
    public static partial int open(string pathname, OpenFlags flags, int mode);

    /// <summary>
    /// Closes a file descriptor.
    /// </summary>
    /// <param name="fd">The file descriptor to close.</param>
    /// <returns>0 on success, or -1 on error.</returns>
    [LibraryImport(LibraryName, EntryPoint = "close", SetLastError = true)]
    public static partial int close(int fd);
}