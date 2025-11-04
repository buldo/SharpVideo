using System.Runtime.Versioning;
using SharpVideo.Drm;

namespace SharpVideo.Utils;

/// <summary>
/// Base exception for DRM-related errors.
/// </summary>
[SupportedOSPlatform("linux")]
public class DrmException : Exception
{
    /// <summary>
    /// DRM device file descriptor (if available).
    /// </summary>
    public int? DrmDeviceFd { get; }

    public DrmException(string message, int? deviceFd = null) 
        : base(message)
    {
        DrmDeviceFd = deviceFd;
    }

    public DrmException(string message, Exception innerException, int? deviceFd = null) 
        : base(message, innerException)
    {
        DrmDeviceFd = deviceFd;
    }
}

/// <summary>
/// Exception thrown when DRM resources (CRTC, encoder, connector) cannot be initialized.
/// </summary>
[SupportedOSPlatform("linux")]
public class DrmResourceNotFoundException : DrmException
{
    public string? ResourceType { get; }

    public DrmResourceNotFoundException(string resourceType, string message, int? deviceFd = null) 
        : base(message, deviceFd)
    {
        ResourceType = resourceType;
    }
}

/// <summary>
/// Exception thrown when a required DRM plane cannot be found.
/// </summary>
[SupportedOSPlatform("linux")]
public class DrmPlaneNotFoundException : DrmResourceNotFoundException
{
    public string PlaneType { get; }
    public PixelFormat? RequiredFormat { get; }

    public DrmPlaneNotFoundException(string planeType, PixelFormat? format, int? deviceFd) 
        : base(
            "Plane",
            BuildMessage(planeType, format),
            deviceFd)
    {
        PlaneType = planeType;
        RequiredFormat = format;
    }

    private static string BuildMessage(string planeType, PixelFormat? format)
    {
        var message = $"No {planeType} plane found";
        if (format != null)
        {
            message += $" with {format.GetName()} format";
        }
        return message;
    }
}

/// <summary>
/// Exception thrown when a required display mode is not available.
/// </summary>
[SupportedOSPlatform("linux")]
public class DrmModeNotFoundException : DrmResourceNotFoundException
{
    public uint RequestedWidth { get; }
    public uint RequestedHeight { get; }

    public DrmModeNotFoundException(uint width, uint height, int? deviceFd) 
        : base(
            "DisplayMode",
            $"No {width}x{height} display mode found",
            deviceFd)
    {
        RequestedWidth = width;
        RequestedHeight = height;
    }
}
