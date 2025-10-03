using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

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

/// <summary>
/// Managed representation of <c>struct media_device_info</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SupportedOSPlatform("linux")]
public unsafe struct MediaDeviceInfo
{
    private fixed byte _driver[16];
    private fixed byte _model[32];
    private fixed byte _serial[40];
    private fixed byte _busInfo[32];
    public uint MediaVersion;
    public uint HwRevision;
    public uint DriverVersion;
    private fixed uint _reserved[31];

    public string DriverString
    {
        get
        {
            fixed (byte* ptr = _driver)
            {
                return ReadNullTerminatedString(ptr, 16);
            }
        }
    }

    public string ModelString
    {
        get
        {
            fixed (byte* ptr = _model)
            {
                return ReadNullTerminatedString(ptr, 32);
            }
        }
    }

    public string SerialString
    {
        get
        {
            fixed (byte* ptr = _serial)
            {
                return ReadNullTerminatedString(ptr, 40);
            }
        }
    }

    public string BusInfoString
    {
        get
        {
            fixed (byte* ptr = _busInfo)
            {
                return ReadNullTerminatedString(ptr, 32);
            }
        }
    }

    private static string ReadNullTerminatedString(byte* buffer, int length)
    {
        var span = new ReadOnlySpan<byte>(buffer, length);
        int terminator = span.IndexOf((byte)0);
        if (terminator < 0)
            terminator = length;
        return Encoding.ASCII.GetString(span[..terminator]);
    }
}
