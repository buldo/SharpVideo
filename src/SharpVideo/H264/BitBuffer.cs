using System;
using System.Diagnostics;

namespace SharpVideo.H264;

/// <summary>
/// A class, similar to ByteBuffer, that can parse bit-sized data out of a set of
/// bytes. Has a similar API to ByteBuffer, plus methods for reading bit-sized
/// and exponential golomb encoded data. For a writable version, use
/// BitBufferWriter. Unlike ByteBuffer, this class doesn't make a copy of the
/// source bytes, so it can be used on read-only data.
/// Sizes/counts specify bits/bytes, for clarity.
/// Byte order is assumed big-endian/network.
/// </summary>
public class BitBuffer
{
    private readonly ReadOnlyMemory<byte> _bytes;

    // The total size of |bytes_|.
    readonly int _byteCount;

    // The current offset, in bytes, from the start of |bytes_|.
    private int _byteOffset;

    // The current offset, in bits, into the current byte.
    private int _bitOffset;

    public BitBuffer(ReadOnlyMemory<byte> bytes)
    {
        _bytes = bytes;
        _byteCount = bytes.Length;
    }

    /// <summary>
    /// Counts the number of bits used in the binary representation of val.
    /// </summary>
    private static int CountBits(UInt64 val)
    {
        int bit_count = 0;
        while (val != 0)
        {
            bit_count++;
            val >>= 1;
        }

        return bit_count;
    }

    /// <summary>
    /// Returns the lowest (right-most) |bit_count| bits in |byte|.
    /// </summary>
    private static byte LowestBits(byte bt, long bit_count)
    {
        //RTC_DCHECK_LE(bit_count, 8);
        return (byte)(bt & ((1 << (byte)bit_count) - 1));
    }

    // Returns the highest (left-most) |bit_count| bits in |byte|, shifted to the
    // lowest bits (to the right).
    private static byte HighestBits(byte bt, long bit_count)
    {
        //RTC_DCHECK_LE(bit_count, 8);
        byte shift = (byte)(8 - (byte)(bit_count));
        byte mask = (byte)(0xFF << shift);
        return (byte)((bt & mask) >> shift);
    }

    /// <summary>
    /// Gets the current offset, in bytes/bits, from the start of the buffer. The
    /// bit offset is the offset into the current byte, in the range [0,7].
    /// </summary>
    public void GetCurrentOffset(out int out_byte_offset, out int out_bit_offset)
    {
        out_byte_offset = _byteOffset;
        out_bit_offset = _bitOffset;
    }

    /// <summary>
    /// The remaining bits in the byte buffer.
    /// </summary>
    public long RemainingBitCount()
    {
        return (_byteCount - _byteOffset) * 8 - _bitOffset;
    }

    /// <summary>
    /// Reads byte-sized values from the buffer.
    /// </summary>
    /// <returns>
    /// false if there isn't enough data left for the specified type
    /// </returns>
    public bool ReadUInt8(out byte val)
    {
        UInt32 bit_val;
        if (!ReadBits(sizeof(byte) * 8, out bit_val))
        {
            val = 0;
            return false;
        }

        //RTC_DCHECK(bit_val <= std::numeric_limits < uint8_t >::max());
        val = (byte)(bit_val);
        return true;
    }

    /// <summary>
    /// Reads byte-sized values from the buffer.
    /// </summary>
    /// <returns>
    /// false if there isn't enough data left for the specified type
    /// </returns>
    public bool ReadUInt16(out UInt16 val)
    {
        UInt32 bit_val;
        if (!ReadBits(sizeof(UInt16) * 8, out bit_val))
        {
            val = 0;
            return false;
        }

        //RTC_DCHECK(bit_val <= std::numeric_limits < uint16_t >::max());
        val = (UInt16)(bit_val);
        return true;
    }

    /// <summary>
    /// Reads byte-sized values from the buffer.
    /// </summary>
    /// <returns>
    /// false if there isn't enough data left for the specified type
    /// </returns>
    public bool ReadUInt32(out UInt32 val)
    {
        return ReadBits(sizeof(UInt32) * 8, out val);
    }

    /// <summary>
    /// Reads bit-sized values from the buffer.
    /// </summary>
    /// <param name="bit_count"></param>
    /// <param name="val"></param>
    /// <returns>false if there isn't enough data left for the specified bit count.</returns>
    public bool ReadBits(int bit_count, out UInt32 val)
    {
        return PeekBits(bit_count, out val) && ConsumeBits(bit_count);
    }

    /// <summary>
    /// Reads bit-sized values from the buffer.
    /// </summary>
    /// <param name="bit_count"></param>
    /// <param name="val"></param>
    /// <returns>false if there isn't enough data left for the specified bit count.</returns>
    public bool ReadBits(int bit_count, out UInt64 val)
    {
        return PeekBits(bit_count, out val) && ConsumeBits(bit_count);
    }

    /// <summary>
    /// Peeks bit-sized values from the buffer.
    /// Doesn't move the current offset.
    /// </summary>
    /// <param name="bit_count"></param>
    /// <param name="val"></param>
    /// <returns>false if there isn't enough data left for the specified number of bits</returns>
    public bool PeekBits(int bitCount, out UInt32 val)
    {
        val = 0;

        if (bitCount > RemainingBitCount() || bitCount > 32)
        {
            return false;
        }

        int byteIndex = _byteOffset;
        int remainingBitsInCurrentByte = 8 - _bitOffset;
        uint bits = LowestBits(_bytes.Span[byteIndex], remainingBitsInCurrentByte);
        byteIndex++;

        if (bitCount < remainingBitsInCurrentByte)
        {
            val = HighestBits((byte)bits, _bitOffset + bitCount);
            return true;
        }

        bitCount -= remainingBitsInCurrentByte;

        while (bitCount >= 8)
        {
            bits = (bits << 8) | _bytes.Span[byteIndex];
            byteIndex++;
            bitCount -= 8;
        }

        if (bitCount > 0)
        {
            bits <<= bitCount;
            bits |= HighestBits(_bytes.Span[byteIndex], bitCount);
        }

        val = bits;
        return true;
    }

    /// <summary>
    /// Peeks bit-sized values from the buffer.
    /// Doesn't move the current offset.
    /// </summary>
    /// <param name="bit_count"></param>
    /// <param name="val"></param>
    /// <returns>false if there isn't enough data left for the specified number of bits</returns>
    public bool PeekBits(int bitCount, out UInt64 val)
    {
        val = 0;

        // TODO(nisse): Could allow bit_count == 0 and always return success. But
        // current code reads one byte beyond end of buffer in the case that
        // RemainingBitCount() == 0 and bit_count == 0.
        System.Diagnostics.Debug.Assert(bitCount > 0);

        if (bitCount > RemainingBitCount() || bitCount > 64)
        {
            return false;
        }

        int byteIndex = _byteOffset; // Локальная копия для PeekBits (не изменяем состояние)
        int remainingBitsInCurrentByte = 8 - _bitOffset;
        ulong bits = LowestBits(_bytes.Span[byteIndex], remainingBitsInCurrentByte);
        byteIndex++;

        // If we're reading fewer bits than what's left in the current byte, just
        // return the portion of this byte that we need.
        if (bitCount < remainingBitsInCurrentByte)
        {
            val = HighestBits((byte)bits, _bitOffset + bitCount);
            return true;
        }

        // Otherwise, subtract what we've read from the bit count and read as many
        // full bytes as we can into bits.
        bitCount -= remainingBitsInCurrentByte;
        while (bitCount >= 8)
        {
            bits = (bits << 8) | _bytes.Span[byteIndex];
            byteIndex++;
            bitCount -= 8;
        }

        // Whatever we have left is smaller than a byte, so grab just the bits we need
        // and shift them into the lowest bits.
        if (bitCount > 0)
        {
            bits <<= bitCount;
            bits |= HighestBits(_bytes.Span[byteIndex], bitCount);
        }

        val = bits;
        return true;
    }


    /// <summary>
    /// Reads value in range [0, num_values - 1].
    /// This encoding is similar to ReadBits(val, Ceil(Log2(num_values)),
    /// but reduces wastage incurred when encoding non-power of two value ranges
    /// Non symmetric values are encoded as:
    /// 1) n = countbits(num_values)
    /// 2) k = (1 << n) - num_values
    /// Value v in range [0, k - 1] is encoded in (n-1) bits.
    /// Value v in range [k, num_values - 1] is encoded as (v+k) in n bits.
    /// https://aomediacodec.github.io/av1-spec/#nsn
    /// </summary>
    /// <param name="num_values"></param>
    /// <param name="val"></param>
    /// <returns>Returns false if there isn't enough data left.</returns>
    public bool ReadNonSymmetric(uint numValues, out uint val)
    {
        val = 0;

        Debug.Assert(numValues > 0);
        Debug.Assert(numValues <= (uint)1 << 31);

        int countBits = CountBits(numValues);
        uint numMinBitsValues = ((uint)1 << countBits) - numValues;

        if (!ReadBits(countBits - 1, out val))
        {
            return false;
        }

        if (val < numMinBitsValues)
        {
            return true;
        }

        if (!ReadBits(1, out uint extraBit))
        {
            return false;
        }

        val = (val << 1) + extraBit - numMinBitsValues;
        return true;
    }

    // TODO
    //public bool ReadNonSymmetric(out UInt32 val, UInt32 num_values)
    //{
    //    return val ? ReadNonSymmetric(num_values, out val) : false;
    //}


    /// <summary>
    /// Reads the exponential golomb encoded value at the current offset.
    /// Exponential golomb values are encoded as:
    /// 1) x = source val + 1
    /// 2) In binary, write [countbits(x) - 1] 0s, then x
    /// To decode, we count the number of leading 0 bits, read that many + 1 bits, and increment the result by 1.
    /// </summary>
    /// <param name="val"></param>
    /// <returns>Returns false if there isn't enough data left for the specified type, or if the value wouldn't fit in a UInt32.</returns>
    public bool ReadExponentialGolomb(out UInt32 val)
    {
        val = 0;

        // Store off the current byte/bit offset, in case we want to restore them due
        // to a failed parse.
        int originalByteOffset = _byteOffset;
        int originalBitOffset = _bitOffset;

        // Count the number of leading 0 bits by peeking/consuming them one at a time.
        int zeroBitCount = 0;
        uint peekedBit;
        while (PeekBits(1, out peekedBit) && peekedBit == 0)
        {
            zeroBitCount++;
            ConsumeBits(1);
        }

        // We should either be at the end of the stream, or the next bit should be 1.
        Debug.Assert(!PeekBits(1, out peekedBit) || peekedBit == 1);

        // The bit count of the value is the number of zeros + 1. Make sure that many
        // bits fits in a uint32_t and that we have enough bits left for it, and then
        // read the value.
        int valueBitCount = zeroBitCount + 1;
        if (valueBitCount > 32 || !ReadBits(valueBitCount, out val))
        {
            Debug.Assert(Seek(originalByteOffset, originalBitOffset));
            return false;
        }
        val -= 1;
        return true;
    }

    /// <summary>
    /// Reads signed exponential golomb values at the current offset.
    /// Signed exponential golomb values are just the unsigned values mapped to the sequence 0, 1, -1, 2, -2, etc. in order.
    /// </summary>
    public bool ReadSignedExponentialGolomb(out Int32 val)
    {
        uint unsigned_val;
        if (!ReadExponentialGolomb(out unsigned_val))
        {
            val = 0;
            return false;
        }
        if ((unsigned_val & 1) == 0)
        {
            val = -(int)(unsigned_val / 2);
        }
        else
        {
            val = (int)(unsigned_val + 1) / 2;
        }
        return true;
    }

    /// <summary>
    /// Moves current position |byte_count| bytes forward.
    /// </summary>
    /// <returns>Returns false if there aren't enough bytes left in the buffer.</returns>
    public bool ConsumeBytes(int byte_count)
    {
        return ConsumeBits(byte_count * 8);
    }

    /// <summary>
    /// Moves current position |bit_count| bits forward
    /// </summary>
    /// <param name="bit_count"></param>
    /// <returns>Returns false if there aren't enough bits left in the buffer.</returns>
    public bool ConsumeBits(int bit_count)
    {
        if (bit_count > RemainingBitCount())
        {
            return false;
        }

        _byteOffset += (int)((_bitOffset + bit_count) / 8);
        _bitOffset = (int)((_bitOffset + bit_count) % 8);
        return true;
    }

    // Sets the current offset to the provied byte/bit offsets. The bit
    // offset is from the given byte, in the range [0,7].
    public bool Seek(int byte_offset, int bit_offset)
    {
        if (byte_offset > _byteCount || bit_offset > 7 || (byte_offset == _byteCount && bit_offset > 0))
        {
            return false;
        }

        _byteOffset = byte_offset;
        _bitOffset = bit_offset;
        return true;
    }
}