namespace SharpVideo.Linux.Native;

[Flags]
public enum MsyncFlags : int
{
    MS_ASYNC = 1,
    MS_SYNC = 4,
    MS_INVALIDATE = 2
}