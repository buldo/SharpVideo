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