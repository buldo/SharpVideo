namespace SharpVideo.Linux.Native;

/// <summary>
/// Logging interface for ioctl operations.
/// </summary>
public interface IIoctlLogger
{
    void LogIoctlCall(int fd, uint request, string operation);
    void LogIoctlSuccess(int fd, uint request, string operation);
    void LogIoctlError(int fd, uint request, string operation, int errorCode, string errorMessage);
}