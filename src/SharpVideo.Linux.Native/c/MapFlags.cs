namespace SharpVideo.Linux.Native.C;

[Flags]
public enum MapFlags : int
{
    MAP_SHARED = 0x01,
    MAP_PRIVATE = 0x02,
    MAP_FIXED = 0x10
}