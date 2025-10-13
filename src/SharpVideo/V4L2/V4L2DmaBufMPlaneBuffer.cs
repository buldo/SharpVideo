using System.Runtime.Versioning;
using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

/// <summary>
/// Represents a V4L2 multi-plane buffer using DMABUF (externally allocated DMA buffer).
/// </summary>
[SupportedOSPlatform("linux")]
public class V4L2DmaBufMPlaneBuffer
{
    private readonly V4L2Plane[] _planes;

    public V4L2DmaBufMPlaneBuffer(
        uint index,
        int[] dmaBufferFds,
        uint[] planeSizes)
    {
        if (dmaBufferFds.Length != planeSizes.Length)
        {
            throw new ArgumentException("Number of FDs must match number of plane sizes");
        }

        Index = index;
        DmaBufferFds = dmaBufferFds;

        _planes = new V4L2Plane[planeSizes.Length];
        for (int i = 0; i < planeSizes.Length; i++)
        {
            _planes[i] = new V4L2Plane
            {
                BytesUsed = 0, // For capture buffers, driver fills this on dequeue
                Length = planeSizes[i],
                Memory = new V4L2Plane.PlaneMemory { Fd = dmaBufferFds[i] },
                DataOffset = 0 // Always 0 for separate plane buffers
            };
        }
    }

    public uint Index { get; }

    public int[] DmaBufferFds { get; }

    public V4L2Plane[] Planes => _planes;

    public V4L2Memory Memory => V4L2Memory.DMABUF;

    /// <summary>
    /// Updates the bytesused field for a specific plane.
    /// </summary>
    public void SetPlaneBytesUsed(int planeIndex, uint bytesUsed)
    {
        if (planeIndex >= 0 && planeIndex < _planes.Length)
        {
            _planes[planeIndex].BytesUsed = bytesUsed;
        }
    }
}
