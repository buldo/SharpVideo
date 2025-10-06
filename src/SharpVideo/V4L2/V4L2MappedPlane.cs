using System.Drawing;

namespace SharpVideo.V4L2;

public class V4L2MappedPlane
{
    public V4L2MappedPlane(IntPtr pointer, uint length)
    {
        Pointer = pointer;
        Length = length;
    }

    public IntPtr Pointer { get; }

    public uint Length { get; }

    public Span<byte> AsSpan()
    {
        unsafe
        {
            return new Span<byte>((void*)Pointer, (int)Length);
        }
    }
}