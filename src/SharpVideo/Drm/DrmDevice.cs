using System.Runtime.Versioning;
using SharpVideo.Linux.Native;

namespace SharpVideo.Drm;

[SupportedOSPlatform("linux")]
public class DrmDevice : IDisposable
{
    private int _deviceFd;
    private bool _disposed;

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

    public bool TrySetClientCapability(DrmClientCapability cap, bool value, out int resultCode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        resultCode = LibDrm.drmSetClientCap(_deviceFd, cap, value ? 1u : 0u);
        return resultCode == 0;
    }

    public DrmCapabilitiesState GetDeviceCapabilities()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        return new DrmCapabilitiesState()
        {
            AddFB2Modifiers = GetOrFailBool(DrmCapability.DRM_CAP_ADDFB2_MODIFIERS),
            AsyncPageFlip = GetOrFailBool(DrmCapability.DRM_CAP_ASYNC_PAGE_FLIP),
            AtomicAsyncPageFlip = GetOrFailBool(DrmCapability.DRM_CAP_ATOMIC_ASYNC_PAGE_FLIP),
            CrtcInVblankEvent = GetOrFailBool(DrmCapability.DRM_CAP_CRTC_IN_VBLANK_EVENT),
            CursorHeight = GetOrFailUInt64(DrmCapability.DRM_CAP_CURSOR_HEIGHT),
            CursorWidth = GetOrFailUInt64(DrmCapability.DRM_CAP_CURSOR_WIDTH),
            DumbBuffer = GetOrFailBool(DrmCapability.DRM_CAP_DUMB_BUFFER),
            DumbPreferShadow = GetOrFailBool(DrmCapability.DRM_CAP_DUMB_PREFER_SHADOW),
            DumbPreferredDepth = GetOrFailUInt64(DrmCapability.DRM_CAP_DUMB_PREFERRED_DEPTH),
            PageFlipTarget = GetOrFailBool(DrmCapability.DRM_CAP_PAGE_FLIP_TARGET),
            Prime = GetOrFailPrime(DrmCapability.DRM_CAP_PRIME),
            SyncObj = GetOrFailBool(DrmCapability.DRM_CAP_SYNCOBJ),
            SyncObjTimeline = GetOrFailBool(DrmCapability.DRM_CAP_SYNCOBJ_TIMELINE),
            TimestampMonotonic = GetOrFailBool(DrmCapability.DRM_CAP_TIMESTAMP_MONOTONIC),
            VblankHighCrtc = GetOrFailBool(DrmCapability.DRM_CAP_VBLANK_HIGH_CRTC),
        };

        bool GetOrFailBool(DrmCapability cap)
        {
            var result = LibDrm.drmGetCap(_deviceFd, cap, out var value);
            if (result != 0)
            {
                throw new Exception($"Failed to get capability {cap}");
            }

            return value == 1;
        }

        UInt64 GetOrFailUInt64(DrmCapability cap)
        {
            var result = LibDrm.drmGetCap(_deviceFd, cap, out var value);
            if (result != 0)
            {
                throw new Exception($"Failed to get capability {cap}");
            }

            return value;
        }

        DrmPrimeCap GetOrFailPrime(DrmCapability cap)
        {
            var result = LibDrm.drmGetCap(_deviceFd, cap, out var value);
            if (result != 0)
            {
                throw new Exception($"Failed to get capability {cap}");
            }

            return (DrmPrimeCap)value;
        }
    }
    public DrmDeviceResources? GetResources()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
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

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources if any
            }

            // Dispose unmanaged resources
            if (_deviceFd >= 0)
            {
                Libc.close(_deviceFd);
                _deviceFd = -1;
            }

            _disposed = true;
        }
    }

    ~DrmDevice()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}