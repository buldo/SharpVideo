namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// V4L2 Control Types
/// </summary>
public enum V4L2CtrlType
{
    Integer = 1,
    Boolean = 2,
    Menu = 3,
    Button = 4,
    Integer64 = 5,
    CtrlClass = 6,
    String = 7,
    Bitmask = 8,
    IntegerMenu = 9,
    U8 = 0x0100,
    U16 = 0x0101,
    U32 = 0x0102
}