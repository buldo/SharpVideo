using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct V4L2QueryMenuItem
    {
        public uint Id;
        public uint Index;
        public fixed byte _name[32];
        public long Value;
        public uint Reserved;

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
}
