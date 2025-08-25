using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Represents the result of an ioctl operation.
/// </summary>
public readonly struct IoctlResult
{
    public bool Success { get; }
    public int ErrorCode { get; }
    public string? ErrorMessage { get; }

    public IoctlResult(bool success, int errorCode = 0, string? errorMessage = null)
    {
        Success = success;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public static IoctlResult CreateSuccess() => new(true);
    public static IoctlResult CreateError(int errorCode, string? message = null) => new(false, errorCode, message);
}

/// <summary>
/// High-level helper class for performing ioctl operations with type safety and error handling.
/// </summary>
[SupportedOSPlatform("linux")]
public static class IoctlHelper
{
    /// <summary>
    /// Performs an ioctl operation with no data transfer.
    /// </summary>
    /// <param name="fd">File descriptor</param>
    /// <param name="request">ioctl request code</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult Ioctl(int fd, uint request)
    {
        var result = Libc.ioctl(fd, request, 0);
        if (result == 0)
        {
            return IoctlResult.CreateSuccess();
        }

        var errorCode = Marshal.GetLastPInvokeError();
        var errorMessage = GetErrorMessage(errorCode);
        return IoctlResult.CreateError(errorCode, errorMessage);
    }

    /// <summary>
    /// Performs an ioctl operation with a pointer argument.
    /// </summary>
    /// <param name="fd">File descriptor</param>
    /// <param name="request">ioctl request code</param>
    /// <param name="argp">Pointer to argument data</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult Ioctl(int fd, uint request, nint argp)
    {
        var result = Libc.ioctl(fd, request, argp);
        if (result == 0)
        {
            return IoctlResult.CreateSuccess();
        }

        var errorCode = Marshal.GetLastPInvokeError();
        var errorMessage = GetErrorMessage(errorCode);
        return IoctlResult.CreateError(errorCode, errorMessage);
    }

    /// <summary>
    /// Performs an ioctl operation with a managed structure.
    /// </summary>
    /// <typeparam name="T">Type of the structure</typeparam>
    /// <param name="fd">File descriptor</param>
    /// <param name="request">ioctl request code</param>
    /// <param name="data">Reference to the structure</param>
    /// <returns>Result of the operation</returns>
    public static unsafe IoctlResult Ioctl<T>(int fd, uint request, ref T data) where T : unmanaged
    {
        fixed (T* ptr = &data)
        {
            return Ioctl(fd, request, (nint)ptr);
        }
    }

    /// <summary>
    /// Performs an ioctl operation with a managed structure (read-only).
    /// </summary>
    /// <typeparam name="T">Type of the structure</typeparam>
    /// <param name="fd">File descriptor</param>
    /// <param name="request">ioctl request code</param>
    /// <param name="data">The structure data</param>
    /// <returns>Result of the operation</returns>
    public static unsafe IoctlResult IoctlReadOnly<T>(int fd, uint request, in T data) where T : unmanaged
    {
        fixed (T* ptr = &data)
        {
            return Ioctl(fd, request, (nint)ptr);
        }
    }

    /// <summary>
    /// Performs an ioctl operation and returns both the result and the modified data.
    /// </summary>
    /// <typeparam name="T">Type of the structure</typeparam>
    /// <param name="fd">File descriptor</param>
    /// <param name="request">ioctl request code</param>
    /// <param name="data">The structure data</param>
    /// <returns>Tuple containing the result and the modified data</returns>
    public static unsafe (IoctlResult Result, T Data) IoctlWithResult<T>(int fd, uint request, T data) where T : unmanaged
    {
        var result = Ioctl(fd, request, ref data);
        return (result, data);
    }

    /// <summary>
    /// Performs an ioctl operation with automatic request code generation for a structure.
    /// </summary>
    /// <typeparam name="T">Type of the structure</typeparam>
    /// <param name="fd">File descriptor</param>
    /// <param name="direction">ioctl direction (IOC_READ, IOC_WRITE, etc.)</param>
    /// <param name="type">Device type</param>
    /// <param name="nr">Request number</param>
    /// <param name="data">Reference to the structure</param>
    /// <returns>Result of the operation</returns>
    public static unsafe IoctlResult IoctlAuto<T>(int fd, uint direction, uint type, uint nr, ref T data) where T : unmanaged
    {
        var request = IoctlConstants.IOC(direction, type, nr, (uint)sizeof(T));
        return Ioctl(fd, request, ref data);
    }

    /// <summary>
    /// Gets a human-readable error message for an errno value.
    /// </summary>
    /// <param name="errno">The error number</param>
    /// <returns>Error message string</returns>
    public static string GetErrorMessage(int errno)
    {
        return errno switch
        {
            1 => "EPERM: Operation not permitted",
            2 => "ENOENT: No such file or directory",
            3 => "ESRCH: No such process",
            4 => "EINTR: Interrupted system call",
            5 => "EIO: I/O error",
            6 => "ENXIO: No such device or address",
            7 => "E2BIG: Argument list too long",
            8 => "ENOEXEC: Exec format error",
            9 => "EBADF: Bad file number",
            10 => "ECHILD: No child processes",
            11 => "EAGAIN: Try again",
            12 => "ENOMEM: Out of memory",
            13 => "EACCES: Permission denied",
            14 => "EFAULT: Bad address",
            15 => "ENOTBLK: Block device required",
            16 => "EBUSY: Device or resource busy",
            17 => "EEXIST: File exists",
            18 => "EXDEV: Cross-device link",
            19 => "ENODEV: No such device",
            20 => "ENOTDIR: Not a directory",
            21 => "EISDIR: Is a directory",
            22 => "EINVAL: Invalid argument",
            23 => "ENFILE: File table overflow",
            24 => "EMFILE: Too many open files",
            25 => "ENOTTY: Not a typewriter",
            26 => "ETXTBSY: Text file busy",
            27 => "EFBIG: File too large",
            28 => "ENOSPC: No space left on device",
            29 => "ESPIPE: Illegal seek",
            30 => "EROFS: Read-only file system",
            31 => "EMLINK: Too many links",
            32 => "EPIPE: Broken pipe",
            _ => $"Unknown error {errno}"
        };
    }
}