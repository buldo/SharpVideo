namespace SharpVideo.RtpPlayerDemo.Rtp;

internal class NetConvert
{
    public static UInt16 DoReverseEndian(UInt16 x)
    {
        //return Convert.ToUInt16((x << 8 & 0xff00) | (x >> 8));
        return BitConverter.ToUInt16(BitConverter.GetBytes(x).Reverse().ToArray(), 0);
    }

    public static uint DoReverseEndian(uint x)
    {
        //return (x << 24 | (x & 0xff00) << 8 | (x & 0xff0000) >> 8 | x >> 24);
        return BitConverter.ToUInt32(BitConverter.GetBytes(x).Reverse().ToArray(), 0);
    }

    public static ulong DoReverseEndian(ulong x)
    {
        //return (x << 56 | (x & 0xff00) << 40 | (x & 0xff0000) << 24 | (x & 0xff000000) << 8 | (x & 0xff00000000) >> 8 | (x & 0xff0000000000) >> 24 | (x & 0xff000000000000) >> 40 | x >> 56);
        return BitConverter.ToUInt64(BitConverter.GetBytes(x).Reverse().ToArray(), 0);
    }
}
