using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

/// <summary>
/// P/Invoke wrapper for media controller ioctls and request handling.
/// </summary>
[SupportedOSPlatform("linux")]
public static class LibMedia
{
    /// <summary>
    /// Query basic media device information.
    /// </summary>
    public static IoctlResult QueryDeviceInfo(int mediaFd, ref MediaDeviceInfo info)
    {
        return IoctlHelper.Ioctl(mediaFd, IoctlConstants.MEDIA_IOC_DEVICE_INFO, ref info);
    }

    /// <summary>
    /// Allocate a new media request from the media controller.
    /// </summary>
    public static (IoctlResult Result, int RequestFd) AllocateRequest(int mediaFd)
    {
        int requestFd = 0;
        var result = IoctlHelper.Ioctl(mediaFd, IoctlConstants.MEDIA_IOC_REQUEST_ALLOC, ref requestFd);
        return (result, requestFd);
    }

    /// <summary>
    /// Queue an allocated media request for execution.
    /// </summary>
    public static IoctlResult QueueRequest(int requestFd)
    {
        return IoctlHelper.Ioctl(requestFd, IoctlConstants.MEDIA_REQUEST_IOC_QUEUE);
    }

    /// <summary>
    /// Re-initialize a media request for reuse.
    /// </summary>
    public static IoctlResult ReinitRequest(int requestFd)
    {
        return IoctlHelper.Ioctl(requestFd, IoctlConstants.MEDIA_REQUEST_IOC_REINIT);
    }
}