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
        int dmaBufferFds,
        uint planeSizes,
        uint planeOffsets)
    {
        Index = index;
        DmaBufferFds = dmaBufferFds;

        _planes = new V4L2Plane[1];
        _planes[0] = new V4L2Plane
        {
            BytesUsed = 0, // For capture buffers, driver fills this on dequeue
            Length = planeSizes,
            Memory = new V4L2Plane.PlaneMemory { Fd = dmaBufferFds },
            DataOffset = planeOffsets // Use provided offset (0 for separate buffers, stride*height for contiguous UV)
        };
    }

    public uint Index { get; }

    public int DmaBufferFds { get; }

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
