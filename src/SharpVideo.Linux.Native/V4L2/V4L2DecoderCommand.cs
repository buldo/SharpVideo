namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// V4L2 decoder commands
/// </summary>
public enum V4L2DecoderCommand : uint
{
    START = 0,
    STOP = 1,
    PAUSE = 2,
    RESUME = 3,
    FLUSH = 4,
}