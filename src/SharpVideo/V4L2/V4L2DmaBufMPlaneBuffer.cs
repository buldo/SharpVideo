using System;
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
        int dmaBufferFd,
        uint planeSizes,
        uint planeOffsets)
    {
        Index = index;
        DmaBufferFd = dmaBufferFd;

        _planes = new V4L2Plane[1];
        _planes[0] = new V4L2Plane
        {
            BytesUsed = 0, // For capture buffers, driver fills this on dequeue
            Length = planeSizes,
            Memory = new V4L2Plane.PlaneMemory { Fd = dmaBufferFd },
            DataOffset = planeOffsets // Use provided offset (0 for separate buffers, stride*height for contiguous UV)
        };
    }

    public uint Index { get; }

    public int DmaBufferFd { get; }

    public V4L2Plane[] Planes => _planes;

    public V4L2Memory Memory => V4L2Memory.DMABUF;

    public void ResetPlanesUsed()
    {
        for (int i = 0; i < Planes.Length; i++)
        {
            _planes[i].BytesUsed = 0;
        }
    }
}
