namespace SharpVideo.Linux.Native.Tests;

/// <summary>
/// Test implementation of IoctlLogger for testing purposes
/// </summary>
internal class TestIoctlLogger : IIoctlLogger
{
    public List<string> LoggedOperations { get; } = new List<string>();

    public void LogIoctlCall(int fd, uint request, string operation)
    {
        LoggedOperations.Add($"Call: fd={fd}, request=0x{request:X8}, op={operation}");
    }

    public void LogIoctlSuccess(int fd, uint request, string operation)
    {
        LoggedOperations.Add($"Success: fd={fd}, request=0x{request:X8}, op={operation}");
    }

    public void LogIoctlError(int fd, uint request, string operation, int errorCode, string errorMessage)
    {
        LoggedOperations.Add($"Error: fd={fd}, request=0x{request:X8}, op={operation}, error={errorCode} ({errorMessage})");
    }
}