using System;

namespace SharpVideo.Linux.Native
{
    [Flags]
    public enum V4L2ControlFlags : uint
    {
        DISABLED = 0x0001,
        GRABBED = 0x0002,
        READ_ONLY = 0x0004,
        UPDATE = 0x0008,
        INACTIVE = 0x0010,
        SLIDER = 0x0020,
        WRITE_ONLY = 0x0040,
        VOLATILE = 0x0080,
        HAS_PAYLOAD = 0x0100,
        EXECUTE_ON_WRITE = 0x0200,
        MODIFY_LAYOUT = 0x0400,
    }
}
