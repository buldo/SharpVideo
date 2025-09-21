namespace SharpVideo.Linux.Native;

/// <summary>
/// V4L2 encoder commands
/// </summary>
public enum V4L2EncoderCommand : uint
{
    START = 0,
    STOP = 1,
    PAUSE = 2,
    RESUME = 3,
}