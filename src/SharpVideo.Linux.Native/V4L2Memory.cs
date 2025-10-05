namespace SharpVideo.Linux.Native;

/// <summary>
/// V4L2 memory types
/// </summary>
public enum V4L2Memory : uint
{
    MMAP = 1,
    USERPTR = 2,
    OVERLAY = 3,
    DMABUF = 4
}