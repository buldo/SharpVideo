using SharpVideo.Linux.Native;
using System.Runtime.Versioning;

namespace SharpVideo.Drm;

[SupportedOSPlatform("linux")]
public class DrmPlane
{
    private readonly int _drmFd;
    public uint Id { get; }
    public uint CrtcId { get; }
    public uint FbId { get; }
    public uint PossibleCrtcs { get; }
    public uint GammaSize { get; }
    public IReadOnlyList<uint> Formats { get; }

    internal unsafe DrmPlane(int drmFd, uint planeId)
    {
        _drmFd = drmFd;
        Id = planeId;

        var planePtr = LibDrm.drmModeGetPlane(_drmFd, planeId);
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

        var propsPtr = LibDrm.drmModeObjectGetProperties(_drmFd, Id, LibDrm.DRM_MODE_OBJECT_PLANE);
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

                var propPtr = LibDrm.drmModeGetProperty(_drmFd, propId);
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
}
