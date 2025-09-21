using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public unsafe struct V4L2QueryCtrl
{
    public uint Id;
    public V4L2CtrlType Type;
    public fixed byte _name[32];
    public int Minimum;
    public int Maximum;
    public int Step;
    public int DefaultValue;
    public V4L2ControlFlags Flags;
    private fixed uint _reserved[2];

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