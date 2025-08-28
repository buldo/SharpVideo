namespace SharpVideo.Linux.Native;

// TODO: Add tests for values
[Flags]
public enum MsyncFlags : int
{
    /// <summary>
    /// Sync memory asynchronously
    /// </summary>
    MS_ASYNC = 1,

    /// <summary>
    /// Synchronous memory sync
    /// </summary>
    MS_SYNC = 0,

    /// <summary>
    /// Invalidate the caches
    /// </summary>
    MS_INVALIDATE = 2
}