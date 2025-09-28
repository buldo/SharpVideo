using System.Net;

namespace SharpVideo.H264;

public class NaluChecksum
{
    /// <summary>
    /// maximum length (in bytes)
    /// </summary>
    const int kMaxLength = 32;

    public static NaluChecksum GetNaluChecksum(BitBuffer bitBuffer)
    {
        // save the bit buffer current state
        int byteOffset = 0;
        int bitOffset = 0;
        bitBuffer.GetCurrentOffset(out byteOffset, out bitOffset);

        var checksum = new NaluChecksum();
        // implement simple IP-like checksum (extended from 16/32 to 32/64 bits)
        // Inspired in [https://stackoverflow.com/questions/26774761](https://stackoverflow.com/questions/26774761)

        // Our algorithm is simple, using a 64 bit accumulator (sum), we add
        // sequential 32 bit words to it, and at the end, fold back all the
        // carry bits from the top 32 bits into the lower 32 bits.

        ulong sum = 0;

        uint val = 0;
        while (bitBuffer.ReadUInt32(out val))
        {
            sum += val;
        }

        // check if there are unread bytes
        int i = 0;
        byte val8 = 0;
        val = 0;
        while (bitBuffer.RemainingBitCount() > 0)
        {
            bitBuffer.ReadUInt8(out val8);
            val |= (uint)(val8 << (8 * (3 - i)));
            i += 1;
        }
        if (i > 0)
        {
            sum += val;
        }

        // add back carry outs from top 32 bits to low 32 bits
        // add hi 32 to low 32
        sum = (sum >> 32) + (sum & 0xffffffff);
        // add carry
        sum += (sum >> 32);
        // truncate to 32 bits and get one's complement
        uint answer = ~(uint)sum;

        // write sum into (generic) checksum buffer (network order)
        checksum.checksum = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)answer));
        checksum.length = 4;

        // return the bit buffer to the original state
        bitBuffer.Seek(byteOffset, bitOffset);

        return checksum;
    }


    public byte[] GetChecksum()
    {
        return checksum;

    }

    public int GetLength() { return length; }

//    public char[] GetPrintableChecksum()
//    {
//#define BUFFER_LEN ((kMaxLength * 2) + 1)
//        static char buffer[BUFFER_LEN];
//        int i = 0;
//        int oi = 0;
//        while (i < length)
//        {
//            oi += snprintf(buffer + oi, BUFFER_LEN - oi, "%02x",
//                static_cast < unsigned char > (checksum[i++]));
//        }
//        buffer[i] = '\0';
//        return buffer;
//    }

    private byte[] checksum = new byte[kMaxLength];
    private int length;
};