using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Console-based logger for ioctl operations.
/// </summary>
[SupportedOSPlatform("linux")]
public class ConsoleIoctlLogger : IIoctlLogger
{
    public void LogIoctlCall(int fd, uint request, string operation)
    {
        Console.WriteLine($"[IOCTL] Calling {operation} on fd={fd}, request=0x{request:X8}");
    }

    public void LogIoctlSuccess(int fd, uint request, string operation)
    {
        Console.WriteLine($"[IOCTL] ✅ {operation} succeeded on fd={fd}, request=0x{request:X8}");
    }

    public void LogIoctlError(int fd, uint request, string operation, int errorCode, string errorMessage)
    {
        Console.WriteLine($"[IOCTL] ❌ {operation} failed on fd={fd}, request=0x{request:X8}, error={errorCode} ({errorMessage})");
    }
}