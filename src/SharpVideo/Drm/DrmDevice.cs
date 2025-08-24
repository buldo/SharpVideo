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
                var encoders = new List<DrmEncoder>();
                foreach (var encId in resources->EncoderIds)
                {
                    var encoder = LibDrm.drmModeGetEncoder(_deviceFd, encId);
                    if (encoder == null)
                    {
                        continue;
                    }

                    encoders.Add(new DrmEncoder()
                    {
                        CrtcId = encoder->CrtcId,
                        EncoderId = encoder->EncoderId,
                        EncoderType = encoder->EncoderType,
                        PossibleClones = encoder->PossibleClones,
                        PossibleCrtcs = encoder->PossibleCrtcs
                    });
                    LibDrm.drmModeFreeEncoder(encoder);
                }


                return new DrmDeviceResources
                {
                    FrameBuffers = resources->FramebufferIds.ToArray(),
                    Crtcs = resources->CrtcIds.ToArray(),
                    Connectors = resources->ConnectorIds.ToArray(),
                    Encoders = encoders,
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
