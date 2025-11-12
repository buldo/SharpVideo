using System.Security.Cryptography;

namespace SharpVideo.RtpPlayerDemo.Rtp;

internal class Crypto
{
    private static readonly RNGCryptoServiceProvider MRandomProvider = new();

    public static UInt16 GetRandomUInt16()
    {
        byte[] uint16Buffer = new byte[2];
        MRandomProvider.GetBytes(uint16Buffer);
        return BitConverter.ToUInt16(uint16Buffer, 0);
    }

    public static UInt32 GetRandomUInt(bool noZero = false)
    {
        byte[] uint32Buffer = new byte[4];
        MRandomProvider.GetBytes(uint32Buffer);
        var randomUint = BitConverter.ToUInt32(uint32Buffer, 0);

        if (noZero && randomUint == 0)
        {
            MRandomProvider.GetBytes(uint32Buffer);
            randomUint = BitConverter.ToUInt32(uint32Buffer, 0);
        }

        return randomUint;
    }
}
