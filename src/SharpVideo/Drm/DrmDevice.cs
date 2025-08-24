using System.Runtime.Versioning;
using SharpVideo.Linux.Native;

namespace SharpVideo.Drm;

[SupportedOSPlatform("linux")]
public class DrmDevice
{
    private int _deviceFd;

    public DrmDevice(int deviceFd)
    {
        _deviceFd = deviceFd;
    }

    public static DrmDevice? Open(string path)
    {
        int deviceFd = Libc.open(path, OpenFlags.O_RDWR);
        if (deviceFd < 0)
        {
            return null;
        }

        unsafe
        {
            var resources = LibDrm.GetResources(deviceFd);
            if(resources == null)
            {
                Libc.close(deviceFd);
                return null;
            }

            LibDrm.FreeResources(resources);
        }

        return new DrmDevice(deviceFd);
    }

    public DrmDeviceResources? GetResources()
    {
        unsafe
        {
            var resources = LibDrm.GetResources(_deviceFd);
            if (resources == null)
            {
                return null;
            }

            try
            {
                return new DrmDeviceResources
                {
                    FrameBuffers = resources->FramebufferIds.ToArray(),
                    Crtcs = resources->CrtcIds.ToArray(),
                    Connectors = resources->ConnectorIds.ToArray(),
                    Encoders = resources->EncoderIds.ToArray(),
                    MinWidth = resources->MinWidth,
                    MaxWidth = resources->MaxWidth,
                    MinHeight = resources->MinHeight,
                    MaxHeight = resources->MaxHeight
                };
            }
            finally
            {
                LibDrm.FreeResources(resources);
            }
        }
    }
}

public class Connector
{

}

public class DrmDeviceResources
{
    /// <summary>
    /// Currently allocated framebuffer objects (i.e., objects that can be attached to a given CRTC or sprite for display).
    /// </summary>
    public required IReadOnlyCollection<UInt32> FrameBuffers { get; init; }

    /// <summary>
    /// List the available CRTCs in the configuration. 
    /// A CRTC is simply an object that can scan out a framebuffer to a display sink, and contains mode timing and relative position information. 
    /// CRTCs drive encoders, which are responsible for converting the pixel stream into a specific display protocol (e.g., MIPI or HDMI).
    /// </summary>
    public required IReadOnlyCollection<UInt32> Crtcs { get; init; }

    /// <summary>
    /// List the available physical connectors on the system.
    /// Note that some of these may not be exposed from the chassis (e.g., LVDS or eDP). 
    /// Connectors are attached to encoders and contain information about the attached display sink (e.g., width and height in mm, subpixel ordering, and various other properties).
    /// </summary>
    public required IReadOnlyCollection<UInt32> Connectors { get; init; }
  
    /// <summary>
    /// List the available encoders on the device. 
    /// Each encoder may be associated with a CRTC, and may be used to drive a particular encoder.
    /// </summary>
    public required IReadOnlyCollection<UInt32> Encoders { get; init; }

    public required int MinWidth { get; init; }

    public required int MaxWidth { get; init; }

    public required int MinHeight { get; init; }

    public required int MaxHeight { get; init; }
}