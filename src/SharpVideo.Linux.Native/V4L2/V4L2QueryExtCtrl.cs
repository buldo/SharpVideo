using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// Mirrors struct v4l2_query_ext_ctrl from Linux kernel headers.
/// Allows discovering extended (including compound) V4L2 controls.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public unsafe struct V4L2QueryExtCtrl
{
    public uint Id;
    public V4L2CtrlType Type;
    public fixed byte _name[32];
    public long Minimum;
    public long Maximum;
    public long Step;
    public long DefaultValue;
    public V4L2ControlFlags Flags;
    public uint ElemSize;
    public uint Elems;
    public uint NrOfDims;
    public fixed uint Dims[4];
    private fixed uint _reserved[32];

    public string Name
    {
        get
        {
            fixed (byte* ptr = _name)
            {
                return Marshal.PtrToStringAnsi((nint)ptr) ?? string.Empty;
            }
        }
    }
}
