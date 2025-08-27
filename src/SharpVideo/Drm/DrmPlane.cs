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
}
