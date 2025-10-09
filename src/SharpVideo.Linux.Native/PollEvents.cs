namespace SharpVideo.Linux.Native;

/// <summary>
/// Poll event flags.
/// </summary>
[Flags]
public enum PollEvents : short
{
    /// <summary>
    /// There is data to read.
    /// </summary>
    POLLIN = 0x0001,

    /// <summary>
    /// There is urgent data to read.
    /// </summary>
    POLLPRI = 0x0002,

    /// <summary>
    /// Writing is now possible.
    /// </summary>
    POLLOUT = 0x0004,

    /// <summary>
    /// Error condition.
    /// </summary>
    POLLERR = 0x0008,

    /// <summary>
    /// Hang up.
    /// </summary>
    POLLHUP = 0x0010,

    /// <summary>
    /// Invalid request.
    /// </summary>
    POLLNVAL = 0x0020
}