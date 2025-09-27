namespace SharpVideo.H264;

public static class H264Common
{
    // some ffmpeg constants
    public const UInt32 kMaxMbWidth = 1055;
    public const UInt32 kMaxMbHeight = 1055;
    public const UInt32 kMaxWidth = (kMaxMbWidth * 16);
    public const UInt32 kMaxHeight = (kMaxMbHeight * 16);
    public const UInt32 kMaxMbPicSize = 139264;


    /// <summary>
    /// NALU packing uses a mechanism to identify the start of a new NALU
    /// based on a 3-byte start code sequence. The idea is that every NALU
    /// starts with the binary string "\x00\x00\x01" ("start code prefix").
    /// In order to avoid the start code prefix to appear by chance in the
    /// middle of a NALU, the NALU is checked, and every appearance of a
    /// start code prefix is replaced by a 4-byte escaped version. The
    /// escaped version consists of adding a "\x03" byte as the third
    /// byte of the string. This means that the "\x00\x00\x01" string,
    /// when it is not a start code prefix, is replaced with the
    /// "\x00\x00\x03\x01" string. Note that we also need to escape the
    /// "\x00\x00\x03" string (using "\x00\x00\x03\x03" instead). For
    /// completeness, "\x00\x00\x00" and "\x00\x00\x02" are also escaped.
    ///
    /// UnescapeRbsp() takes a escaped string (where any 3-byte string
    /// where the first 2x bytes are "\x00" and the third byte is "\x00",
    /// "\x01", "\x02", or "\x03" have been escaped (and extra "\x03"
    /// has been inserted as third byte), and returns the unescaped one.
    /// </summary>
    public static List<byte> UnescapeRbsp(ReadOnlySpan<byte> data)
    {
        // TODO Replace to byte[] + Span<>orMemory<>
        List<byte> ret = new List<byte>(data.Length);

        for (int i = 0; i < data.Length;)
        {
            // Be careful about over/underflow here. byte_length_ - 3 can underflow, and
            // i + 3 can overflow, but byte_length_ - i can't, because i < byte_length_
            // above, and that expression will produce the number of bytes left in
            // the stream including the byte at i.
            if (data.Length - i >= 3 &&
                data[i] == 0x00 &&
                data[i + 1] == 0x00 &&
                data[i + 2] == 0x03)
            {
                // Two RBSP bytes.
                ret.Add(data[i++]);
                ret.Add(data[i++]);
                // Skip the emulation byte.
                i++;
            }
            else
            {
                // Single rbsp byte.
                ret.Add(data[i++]);
            }
        }

        return ret;
    }

    public static bool MoreRbspData(BitBuffer bitBuffer)
    {
        // > If there is no more data in the raw byte sequence payload (RBSP), the
        // > return value of more_rbsp_data() is equal to FALSE.
        var remainingBitcount = bitBuffer.RemainingBitCount();
        if (remainingBitcount == 0)
        {
            return false;
        }

        // > Otherwise, the RBSP data is searched for the last (least significant,
        // > right-most) bit equal to 1 that is present in the RBSP. Given the
        // > position of this bit, which is the first bit (rbsp_stop_one_bit) of
        // > the rbsp_trailing_bits() syntax structure, the following applies:
        // > - If there is more data in an RBSP before the rbsp_trailing_bits()
        // >   syntax structure, the return value of more_rbsp_data() is equal to
        // >   TRUE.
        // > - Otherwise, the return value of more_rbsp_data() is equal to FALSE.
        // > The method for enabling determination of whether there is more data
        // > in the RBSP is specified by the application (or in Annex B for
        // > applications that use the byte stream format).

        // Here we do the following simplification:
        // (1) We know that rbsp_trailing_bits() is limited to at most 1 byte. Its
        // definition is:
        // > rbsp_trailing_bits() {
        // >   rbsp_stop_one_bit // equal to 1
        // >   while( !byte_aligned() )
        // >     rbsp_alignment_zero_bit // equal to 0
        // >   }
        // where byte_aligned() is a Bool stating whether the position of the
        // bitstream is in a byte boundary. So, if there is more than 1 byte
        // left (8 bits left), clearly "there is more data in the RBSP before the
        // rbsp_trailing_bits()"
        if (remainingBitcount > 8)
        {
            return true;
        }

        // (2) if we are indeed in the last byte, we just need to know whether the
        // rest of the byte is [1, 0, ..., 0]. For that, we want to peek in the
        // bit buffer (not read).
        // So we first read (peek) the remaining bits.
        uint remainingBits;
        if (!bitBuffer.PeekBits((int)remainingBitcount, out remainingBits))
        {
            // this should not happen: we do not have remaining_bits bits left.
            return false;
        }
        // and then check for the actual values to be 100..000
        bool isRbspTrailingBits =
            (remainingBits == (1u << ((int)remainingBitcount - 1)));

        // if the actual values to be 100..000, we are already at the
        // rbsp_trailing_bits, which means there is no more RBSP data
        return !isRbspTrailingBits;
    }

    public static bool rbsp_trailing_bits(BitBuffer bitBuffer)
    {
        uint bits_tmp;

        // rbsp_stop_one_bit  f(1) // equal to 1
        if (!bitBuffer.ReadBits(1, out bits_tmp))
        {
            return false;
        }
        if (bits_tmp != 1)
        {
            return false;
        }

        while (!byte_aligned(bitBuffer))
        {
            // rbsp_alignment_zero_bit  f(1) // equal to 0
            if (!bitBuffer.ReadBits(1, out bits_tmp))
            {
                return false;
            }
            if (bits_tmp != 0)
            {
                return false;
            }
        }
        return true;
    }

    // Syntax functions and descriptors) (Section 7.2)
    internal static bool byte_aligned(BitBuffer bit_buffer)
    {
        // If the current position in the bitstream is on a byte boundary, i.e.,
        // the next bit in the bitstream is the first bit in a byte, the return
        // value of byte_aligned() is equal to TRUE.
        // Otherwise, the return value of byte_aligned() is equal to FALSE.
        int out_byte_offset, out_bit_offset;
        bit_buffer.GetCurrentOffset(out out_byte_offset, out out_bit_offset);

        return (out_bit_offset == 0);
    }
}