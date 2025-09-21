namespace SharpVideo.Linux.Native;

/// <summary>
/// V4L2 field definitions
/// </summary>
public enum V4L2Field : uint
{
    ANY = 0,
    NONE = 1,
    TOP = 2,
    BOTTOM = 3,
    INTERLACED = 4,
    SEQ_TB = 5,
    SEQ_BT = 6,
    ALTERNATE = 7,
    INTERLACED_TB = 8,
    INTERLACED_BT = 9,
}