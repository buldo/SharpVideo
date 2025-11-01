using SharpVideo.Linux.Native;
using System.Runtime.Versioning;

namespace SharpVideo.Drm;

[SupportedOSPlatform("linux")]
public class DrmPlane
{
    private readonly int _deviceFd;
    public uint Id { get; }
    public uint CrtcId { get; }
    public uint FbId { get; }
    public uint PossibleCrtcs { get; }
    public uint GammaSize { get; }
    public IReadOnlyList<uint> Formats { get; }

    internal unsafe DrmPlane(int deviceFd, uint planeId)
    {
        _deviceFd = deviceFd;
        Id = planeId;

        var planePtr = LibDrm.drmModeGetPlane(_deviceFd, planeId);
        if (planePtr == null)
        {
            throw new InvalidOperationException($"Failed to get plane {planeId}");
        }

        try
        {
            CrtcId = planePtr->CrtcId;
            FbId = planePtr->FbId;
            PossibleCrtcs = planePtr->PossibleCrtcs;
            GammaSize = planePtr->GammaSize;

            var formats = new List<uint>();
            for (int i = 0; i < planePtr->CountFormats; i++)
            {
                formats.Add(planePtr->Formats[i]);
            }
            Formats = formats;
        }
        finally
        {
            LibDrm.drmModeFreePlane(planePtr);
        }
    }

    /// <summary>
    /// Gets all properties for this plane with their current values.
    /// </summary>
    public unsafe List<DrmProperty> GetProperties()
    {
        var properties = new List<DrmProperty>();

        var propsPtr = LibDrm.drmModeObjectGetProperties(_deviceFd, Id, LibDrm.DRM_MODE_OBJECT_PLANE);
        if (propsPtr == null)
        {
            return properties;
        }

        try
        {
            var propIds = propsPtr->Props;
            var propValues = propsPtr->PropValues;

            for (int i = 0; i < propsPtr->CountProps; i++)
            {
                var propId = propIds[i];
                var propValue = propValues[i];

                var propPtr = LibDrm.drmModeGetProperty(_deviceFd, propId);
                if (propPtr == null)
                {
                    continue;
                }

                try
                {
                    var values = new List<ulong>();
                    foreach (var val in propPtr->Values)
                    {
                        values.Add(val);
                    }

                    var blobIds = new List<uint>();
                    foreach (var blobId in propPtr->BlobIds)
                    {
                        blobIds.Add(blobId);
                    }

                    var enumNames = new List<string>();
                    foreach (var enumVal in propPtr->Enums)
                    {
                        enumNames.Add(enumVal.NameString);
                    }

                    properties.Add(new DrmProperty
                    {
                        Id = propPtr->PropId,
                        Name = propPtr->NameString,
                        Type = propPtr->Type,
                        Value = propValue,
                        Flags = propPtr->Flags,
                        Values = values.Count > 0 ? values : null,
                        EnumNames = enumNames.Count > 0 ? enumNames : null,
                        BlobIds = blobIds.Count > 0 ? blobIds : null
                    });
                }
                finally
                {
                    LibDrm.drmModeFreeProperty(propPtr);
                }
            }
        }
        finally
        {
            LibDrm.drmModeFreeObjectProperties(propsPtr);
        }

        return properties;
    }

    public unsafe uint GetPlanePropertyId(string propertyName)
    {
        var props = LibDrm.drmModeObjectGetProperties(_deviceFd, Id, LibDrm.DRM_MODE_OBJECT_PLANE);
        if (props == null)
            return 0;

        try
        {
            for (int i = 0; i < props->CountProps; i++)
            {
                var propId = props->Props[i];
                var prop = LibDrm.drmModeGetProperty(_deviceFd, propId);
                if (prop == null)
                    continue;

                try
                {
                    var name = prop->NameString;
                    if (name != null && name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        return propId;
                    }
                }
                finally
                {
                    LibDrm.drmModeFreeProperty(prop);
                }
            }
        }
        finally
        {
            LibDrm.drmModeFreeObjectProperties(props);
        }

        return 0;
    }

    /// <summary>
    /// Sets the z-position property for a plane to control layering order.
    /// Lower z-pos values are displayed behind higher values.
    /// </summary>
    public bool SetPlaneZPosition(ulong zpos)
    {
        var zposPropertyId = GetPlanePropertyId("zpos");
        if (zposPropertyId == 0)
        {
            //_logger.LogWarning("Plane {PlaneId} does not have zpos property", planeId);
            return false;
        }

        var result = LibDrm.drmModeObjectSetProperty(
            _deviceFd,
            Id,
            LibDrm.DRM_MODE_OBJECT_PLANE,
            zposPropertyId,
            zpos);

        if (result != 0)
        {
            //_logger.LogError("Failed to set zpos={Zpos} for plane {PlaneId}: {Result}", zpos, planeId, result);
            return false;
        }

        //_logger.LogInformation("Set plane {PlaneId} zpos to {Zpos}", planeId, zpos);
        return true;
    }

    /// <summary>
    /// Gets the valid range for the zpos property of a plane.
    /// Returns (min, max, current) or null if zpos is not supported.
    /// </summary>
    public unsafe (ulong min, ulong max, ulong current)? GetPlaneZPositionRange()
    {
        var props = LibDrm.drmModeObjectGetProperties(_deviceFd, Id, LibDrm.DRM_MODE_OBJECT_PLANE);
        if (props == null)
            return null;

        try
        {
            for (int i = 0; i < props->CountProps; i++)
            {
                var propId = props->Props[i];
                var prop = LibDrm.drmModeGetProperty(_deviceFd, propId);
                if (prop == null)
                    continue;

                try
                {
                    var name = prop->NameString;
                    if (name != null && name.Equals("zpos", StringComparison.OrdinalIgnoreCase))
                    {
                        var currentValue = props->PropValues[i];

                        // For range properties, values[0] is min and values[1] is max
                        if (prop->CountValues >= 2)
                        {
                            var min = prop->Values[0];
                            var max = prop->Values[1];
                            return (min, max, currentValue);
                        }
                    }
                }
                finally
                {
                    LibDrm.drmModeFreeProperty(prop);
                }
            }
        }
        finally
        {
            LibDrm.drmModeFreeObjectProperties(props);
        }

        return null;
    }

}
