using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Enhanced ioctl helper with logging and detailed error reporting.
/// </summary>
[SupportedOSPlatform("linux")]
public static class IoctlHelperWithLogging
{
    private static IIoctlLogger? _logger;

    /// <summary>
    /// Sets the logger for ioctl operations.
    /// </summary>
    /// <param name="logger">Logger instance, or null to disable logging</param>
    public static void SetLogger(IIoctlLogger? logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Performs an ioctl operation with logging and enhanced error reporting.
    /// </summary>
    /// <param name="fd">File descriptor</param>
    /// <param name="request">ioctl request code</param>
    /// <param name="operationName">Human-readable operation name for logging</param>
    /// <returns>Enhanced result with detailed error information</returns>
    public static IoctlResultWithDetails Ioctl(int fd, uint request, string operationName = "ioctl")
    {
        _logger?.LogIoctlCall(fd, request, operationName);

        var result = Libc.ioctl(fd, request, 0);
        
        if (result == 0)
        {
            _logger?.LogIoctlSuccess(fd, request, operationName);
            return IoctlResultWithDetails.CreateSuccess(operationName);
        }

        var errorCode = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
        var errorMessage = GetDetailedErrorMessage(errorCode);
        
        _logger?.LogIoctlError(fd, request, operationName, errorCode, errorMessage);
        
        return IoctlResultWithDetails.CreateError(operationName, errorCode, errorMessage, GetErrorSuggestion(errorCode));
    }

    /// <summary>
    /// Performs an ioctl operation with a pointer argument, logging, and enhanced error reporting.
    /// </summary>
    /// <param name="fd">File descriptor</param>
    /// <param name="request">ioctl request code</param>
    /// <param name="argp">Pointer to argument data</param>
    /// <param name="operationName">Human-readable operation name for logging</param>
    /// <returns>Enhanced result with detailed error information</returns>
    public static IoctlResultWithDetails Ioctl(int fd, uint request, nint argp, string operationName = "ioctl")
    {
        _logger?.LogIoctlCall(fd, request, operationName);

        var result = Libc.ioctl(fd, request, argp);
        
        if (result == 0)
        {
            _logger?.LogIoctlSuccess(fd, request, operationName);
            return IoctlResultWithDetails.CreateSuccess(operationName);
        }

        var errorCode = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
        var errorMessage = GetDetailedErrorMessage(errorCode);
        
        _logger?.LogIoctlError(fd, request, operationName, errorCode, errorMessage);
        
        return IoctlResultWithDetails.CreateError(operationName, errorCode, errorMessage, GetErrorSuggestion(errorCode));
    }

    /// <summary>
    /// Gets a detailed error message with additional context.
    /// </summary>
    private static string GetDetailedErrorMessage(int errno)
    {
        return errno switch
        {
            1 => "EPERM: Operation not permitted - Check process permissions",
            2 => "ENOENT: No such file or directory - Device may not exist",
            9 => "EBADF: Bad file descriptor - File descriptor may be closed or invalid",
            13 => "EACCES: Permission denied - Insufficient privileges to perform operation",
            14 => "EFAULT: Bad address - Invalid pointer passed to ioctl",
            16 => "EBUSY: Device or resource busy - Device is currently in use",
            19 => "ENODEV: No such device - Device driver not loaded or device not present",
            22 => "EINVAL: Invalid argument - Invalid ioctl request or argument",
            25 => "ENOTTY: Inappropriate ioctl for device - ioctl not supported by this device",
            _ => $"Error {errno}: {IoctlHelper.GetErrorMessage(errno)}"
        };
    }

    /// <summary>
    /// Gets a suggestion for resolving the error.
    /// </summary>
    private static string GetErrorSuggestion(int errno)
    {
        return errno switch
        {
            1 => "Try running with elevated privileges (sudo)",
            2 => "Verify the device path exists and is accessible",
            9 => "Ensure the file descriptor is valid and not closed",
            13 => "Check file permissions or run with appropriate privileges",
            14 => "Verify all pointers are valid and properly allocated",
            16 => "Wait for the device to become available or close other processes using it",
            19 => "Load the appropriate kernel module or check hardware connection",
            22 => "Verify the ioctl request code and argument structure",
            25 => "Check if the ioctl is supported by this device type",
            _ => "Consult system documentation or kernel logs for more information"
        };
    }
}