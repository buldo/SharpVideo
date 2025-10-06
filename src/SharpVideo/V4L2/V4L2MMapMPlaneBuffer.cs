using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

public class V4L2MMapMPlaneBuffer
{
    private readonly V4L2Buffer _buffer;
    private readonly V4L2Plane[] _planes;
    private readonly List<V4L2MappedPlane> _mappedPlanes;

    public V4L2MMapMPlaneBuffer(
        V4L2Buffer buffer,
        V4L2Plane[] planes,
        List<V4L2MappedPlane> mappedPlanes)
    {
        _buffer = buffer;
        _planes = planes;
        _mappedPlanes = mappedPlanes;
    }

    public V4L2Plane[] Planes => _planes;

    public IReadOnlyList<V4L2MappedPlane> MappedPlanes => _mappedPlanes;

    public uint Index => _buffer.Index;

    public V4L2Memory Memory => V4L2Memory.MMAP;

    public void CopyDataToPlane(ReadOnlySpan<byte> frameData, int planeNum)
    {
        frameData.CopyTo(_mappedPlanes[planeNum].AsSpan());
        _planes[planeNum].DataOffset = 0;
        _planes[planeNum].BytesUsed = (uint)frameData.Length;
    }
}