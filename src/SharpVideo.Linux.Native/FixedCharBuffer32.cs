using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct FixedCharBuffer32
    {
        private fixed byte _buffer[32];

        public override string ToString()
        {
            fixed (byte* ptr = _buffer)
            {
                return Marshal.PtrToStringAnsi((System.IntPtr)ptr);
            }
        }
    }
}
