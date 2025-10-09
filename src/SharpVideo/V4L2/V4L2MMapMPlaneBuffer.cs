using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

[SupportedOSPlatform("linux")]
public class V4L2MMapMPlaneBuffer
{
    private readonly int _deviceFd;
    private readonly V4L2Buffer _buffer;
    private readonly V4L2Plane[] _planes;

    private bool _isMapped;
    private List<V4L2MappedPlane>? _mappedPlanes;

    public V4L2MMapMPlaneBuffer(
        int deviceFd,
        V4L2Buffer buffer,
        V4L2Plane[] planes)
    {
        _deviceFd = deviceFd;
        _buffer = buffer;
        _planes = planes;
    }

    public V4L2Plane[] Planes => _planes;

    public IReadOnlyList<V4L2MappedPlane> MappedPlanes => _isMapped ? _mappedPlanes! : throw new Exception("Planes not mapped");

    public uint Index => _buffer.Index;

    public V4L2Memory Memory => V4L2Memory.MMAP;

    public void CopyDataToPlane(ReadOnlySpan<byte> frameData, int planeNum)
    {
        frameData.CopyTo(_mappedPlanes[planeNum].AsSpan());
        _planes[planeNum].DataOffset = 0;
        _planes[planeNum].BytesUsed = (uint)frameData.Length;
    }

    public void MapToMemory()
    {
        if (_isMapped)
        {
            throw new Exception("Already mapped");
        }

        var mappedPlanes = new List<V4L2MappedPlane>();
        // 3.1 We are mapping each plane
        foreach (var plane in _planes)
        {
            var mapped = Libc.mmap(
                IntPtr.Zero,
                plane.Length,
                ProtFlags.PROT_READ | ProtFlags.PROT_WRITE,
                MapFlags.MAP_SHARED, _deviceFd, (nint)plane.Memory.MemOffset);
            if (mapped == Libc.MAP_FAILED)
            {
                throw new Exception("Failed to map buffer plane");
            }
            mappedPlanes.Add(new V4L2MappedPlane(mapped, plane.Length));
        }

        _mappedPlanes = mappedPlanes;
        _isMapped = true;
    }

    public void Unmap()
    {
        if (!_isMapped)
        {
            throw new Exception("Buffer was not mapped");
        }

        foreach (var plane in MappedPlanes)
        {
            var planePtr = plane.Pointer;
            if (planePtr == IntPtr.Zero)
            {
                continue;
            }

            unsafe
            {
                var result = Libc.munmap((void*)planePtr, plane.Length);
                if (result != 0)
                {
                    // TODO: error handling
                    continue;
                }
            }
        }
    }
}