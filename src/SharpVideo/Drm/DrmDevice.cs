using System.Runtime.Versioning;
using SharpVideo.Linux.Native;

namespace SharpVideo.Drm;

// TODO: Add dispose
[SupportedOSPlatform("linux")]
public class DrmDevice
{
    private int _deviceFd;

    /// <summary>
    /// Gets the device file descriptor.
    /// </summary>
    public int DeviceFd => _deviceFd;

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

        // Enable universal planes capability to expose all planes (including primary planes)
        var capResult = LibDrm.drmSetClientCap(deviceFd, LibDrm.DRM_CLIENT_CAP_UNIVERSAL_PLANES, 1);
        if (capResult != 0)
        {
            Console.WriteLine($"Warning: Failed to enable universal planes capability: {capResult}");
            Console.WriteLine("This may result in primary planes not being visible.");
        }
        else
        {
            Console.WriteLine("Successfully enabled universal planes capability");
        }

        unsafe
        {
            var resources = LibDrm.drmModeGetResources(deviceFd);
            if(resources == null)
            {
                Libc.close(deviceFd);
                return null;
            }

            LibDrm.drmModeFreeResources(resources);
        }

        return new DrmDevice(deviceFd);
    }

    public DrmDeviceResources? GetResources()
    {
        unsafe
        {
            var resources = LibDrm.drmModeGetResources(_deviceFd);
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

                    encoders.Add(new DrmEncoder
                    {
                        CrtcId = encoder->CrtcId,
                        EncoderId = encoder->EncoderId,
                        EncoderType = encoder->EncoderType,
                        PossibleClones = encoder->PossibleClones,
                        PossibleCrtcs = encoder->PossibleCrtcs
                    });
                    LibDrm.drmModeFreeEncoder(encoder);
                }

                var encodersById = encoders.ToDictionary(en => en.EncoderId);

                var connectors = new List<DrmConnector>();
                foreach (var connId in resources->ConnectorIds)
                {
                    var connector = LibDrm.drmModeGetConnector(_deviceFd, connId);
                    if (connector == null)
                    {
                        continue;
                    }

                    var currentEncoder = encodersById.GetValueOrDefault(connector->EncoderId);

                    var connectorEncoders = new List<DrmEncoder>();
                    foreach (var encId in connector->Encoders)
                    {
                        if(encodersById.TryGetValue(encId, out var encoder))
                        {
                            connectorEncoders.Add(encoder);
                        }
                    }

                    var connectorModes = new List<DrmModeInfo>();
                    foreach (var mode in connector->Modes)
                    {
                        connectorModes.Add(new DrmModeInfo
                        {
                            Clock = mode.Clock,
                            HDisplay = mode.HDisplay,
                            HSyncStart = mode.HSyncStart,
                            HSyncEnd = mode.HSyncEnd,
                            HTotal = mode.HTotal,
                            HSkew = mode.HSkew,
                            VDisplay = mode.VDisplay,
                            VSyncStart = mode.VSyncStart,
                            VSyncEnd = mode.VSyncEnd,
                            VTotal = mode.VTotal,
                            VScan = mode.VScan,
                            VRefresh = mode.VRefresh,
                            Flags = mode.Flags,
                            Type = mode.Type,
                            Name = mode.NameString
                        });
                    }

                    var props = new List<DrmProperty>();
                    var origProps = connector->Props;
                    var origValues = connector->PropValues;
                    for (int i = 0; i < connector->CountProps; i++)
                    {
                        var prop = LibDrm.drmModeGetProperty(_deviceFd, origProps[i]);
                        if (prop == null)
                        {
                            continue;
                        }
                        props.Add(new DrmProperty
                        {
                            Id = prop->PropId,
                            Name = prop->NameString,
                            Type = (PropertyType)prop->Flags,
                            Value = origValues[i],
                        });

                        LibDrm.drmModeFreeProperty(prop);
                    }

                    connectors.Add(new DrmConnector
                    {
                        Connection = connector->Connection,
                        ConnectorId = connector->ConnectorId,
                        ConnectorType = connector->ConnectorType,
                        ConnectorTypeId = connector->ConnectorTypeId,
                        MmHeight = connector->MmHeight,
                        MmWidth = connector->MmWidth,
                        Encoder = currentEncoder,
                        SubPixel = connector->SubPixel,
                        Encoders = connectorEncoders,
                        Modes = connectorModes,
                        Props = props
                    });
                    LibDrm.drmModeFreeConnector(connector);
                }

                var planeResources = LibDrm.drmModeGetPlaneResources(_deviceFd);
                var planes = new List<DrmPlane>();
                if (planeResources != null)
                {
                    try
                    {
                        Console.WriteLine($"DEBUG: drmModeGetPlaneResources returned {planeResources->CountPlanes} planes");
                        var planeIds = planeResources->Planes;
                        Console.WriteLine($"DEBUG: Plane IDs from native: {string.Join(", ", planeIds.ToArray())}");

                        foreach (var planeId in planeIds)
                        {
                            Console.WriteLine($"DEBUG: Loading plane ID {planeId}");
                            try
                            {
                                planes.Add(new DrmPlane(_deviceFd, planeId));
                                Console.WriteLine($"DEBUG: Successfully loaded plane {planeId}");
                            }
                            catch (Exception ex)
                            {
                                // Log and continue - don't let one bad plane stop us from loading others
                                Console.WriteLine($"Warning: Failed to load plane {planeId}: {ex.Message}");
                            }
                        }

                        Console.WriteLine($"DEBUG: Total planes loaded into list: {planes.Count}");
                    }
                    finally
                    {
                        LibDrm.drmModeFreePlaneResources(planeResources);
                    }
                }

                return new DrmDeviceResources
                {
                    FrameBuffers = resources->FramebufferIds.ToArray(),
                    Crtcs = resources->CrtcIds.ToArray(),
                    Connectors = connectors,
                    Encoders = encoders,
                    Planes = planes,
                    MinWidth = resources->MinWidth,
                    MaxWidth = resources->MaxWidth,
                    MinHeight = resources->MinHeight,
                    MaxHeight = resources->MaxHeight
                };
            }
            finally
            {
                LibDrm.drmModeFreeResources(resources);
            }
        }
    }
}