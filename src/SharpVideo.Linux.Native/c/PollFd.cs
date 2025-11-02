using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native.C;

/// <summary>
/// Structure for poll() system call.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PollFd
{
    /// <summary>
    /// File descriptor to poll.
    /// </summary>
    public int fd;

    /// <summary>
    /// Events to poll for (input).
    /// </summary>
    public PollEvents events;

    /// <summary>
    /// Events that occurred (output).
    /// </summary>
    public PollEvents revents;
}