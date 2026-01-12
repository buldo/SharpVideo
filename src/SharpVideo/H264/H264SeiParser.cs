namespace SharpVideo.H264;

/// <summary>
/// Parser for H.264 SEI (Supplemental Enhancement Information) messages.
/// Based on section 7.3.2.3 of the H.264/AVC specification.
/// </summary>
public static class H264SeiParser
{
    /// <summary>
    /// Parse SEI RBSP from a bit buffer.
    /// </summary>
    /// <param name="bitBuffer">The bit buffer containing the SEI data.</param>
    /// <param name="parserState">The bitstream parser state containing SPS information.</param>
    /// <returns>The parsed SEI state containing all messages.</returns>
    public static SeiRbspState ParseSeiRbsp(BitBuffer bitBuffer, H264BitstreamParserState parserState)
    {
        var state = new SeiRbspState();

        // Parse SEI messages until RBSP trailing bits
        // sei_rbsp( ) Descriptor
        // do
        //   sei_message( )
        // while( more_rbsp_data( ) )
        // rbsp_trailing_bits( )

        do
        {
            var message = ParseSeiMessage(bitBuffer, parserState);
            if (message != null)
            {
                state.Messages.Add(message);
            }
            else
            {
                break;
            }
        }
        while (H264Common.MoreRbspData(bitBuffer));

        return state;
    }

    /// <summary>
    /// Parse a single SEI message.
    /// </summary>
    private static SeiMessageState? ParseSeiMessage(BitBuffer bitBuffer, H264BitstreamParserState parserState)
    {
        if (bitBuffer.BitsRemaining < 16)
        {
            return null;
        }

        // sei_message( ) {
        //   payloadType = 0
        //   while( next_bits( 8 ) = = 0xFF ) {
        //     ff_byte /* equal to 0xFF */
        //     payloadType += 255
        //   }
        //   last_payload_type_byte
        //   payloadType += last_payload_type_byte

        uint payloadType = 0;
        while (bitBuffer.BitsRemaining >= 8)
        {
            if (!bitBuffer.ReadBits(8, out uint nextByte))
            {
                return null;
            }
            payloadType += nextByte;
            if (nextByte != 0xFF)
            {
                break;
            }
        }

        //   payloadSize = 0
        //   while( next_bits( 8 ) = = 0xFF ) {
        //     ff_byte /* equal to 0xFF */
        //     payloadSize += 255
        //   }
        //   last_payload_size_byte
        //   payloadSize += last_payload_size_byte
        //   sei_payload( payloadType, payloadSize )
        // }

        uint payloadSize = 0;
        while (bitBuffer.BitsRemaining >= 8)
        {
            if (!bitBuffer.ReadBits(8, out uint nextByte))
            {
                return null;
            }
            payloadSize += nextByte;
            if (nextByte != 0xFF)
            {
                break;
            }
        }

        var message = new SeiMessageState
        {
            PayloadType = (SeiPayloadType)payloadType,
            PayloadSize = payloadSize
        };

        // Calculate the end position of this payload
        long payloadEndBit = bitBuffer.BitsRemaining - (payloadSize * 8);

        // Parse specific payload types
        switch (message.PayloadType)
        {
            case SeiPayloadType.RecoveryPoint:
                message.RecoveryPoint = ParseRecoveryPoint(bitBuffer, parserState);
                break;

            default:
                // Skip unsupported payload types
                if (payloadSize > 0 && bitBuffer.BitsRemaining >= payloadSize * 8)
                {
                    bitBuffer.SkipBits((int)(payloadSize * 8));
                }
                break;
        }

        // Align to byte boundary after parsing payload (if not aligned)
        // This handles bit_equal_to_one and bit_equal_to_zero in sei_payload alignment
        long currentRemaining = bitBuffer.BitsRemaining;
        if (currentRemaining > payloadEndBit)
        {
            int bitsToSkip = (int)(currentRemaining - payloadEndBit);
            if (bitsToSkip > 0 && bitsToSkip < 64)
            {
                bitBuffer.SkipBits(bitsToSkip);
            }
        }

        return message;
    }

    /// <summary>
    /// Parse recovery point SEI message.
    /// Section D.1.7 of the H.264/AVC specification.
    /// </summary>
    private static RecoveryPointState? ParseRecoveryPoint(BitBuffer bitBuffer, H264BitstreamParserState parserState)
    {
        // recovery_point( payloadSize ) {
        //   recovery_frame_cnt ue(v)
        //   exact_match_flag u(1)
        //   broken_link_flag u(1)
        //   changing_slice_group_idc u(2)
        // }

        if (bitBuffer.BitsRemaining < 4)
        {
            return null;
        }

        var state = new RecoveryPointState();

        // recovery_frame_cnt - ue(v)
        if (!bitBuffer.ReadExponentialGolomb(out uint recoveryFrameCnt))
        {
            return null;
        }
        state.RecoveryFrameCnt = recoveryFrameCnt;

        // exact_match_flag - u(1)
        if (!bitBuffer.ReadBits(1, out uint exactMatch))
        {
            return null;
        }
        state.ExactMatchFlag = exactMatch != 0;

        // broken_link_flag - u(1)
        if (!bitBuffer.ReadBits(1, out uint brokenLink))
        {
            return null;
        }
        state.BrokenLinkFlag = brokenLink != 0;

        // changing_slice_group_idc - u(2)
        if (!bitBuffer.ReadBits(2, out uint changingSliceGroupIdc))
        {
            return null;
        }
        state.ChangingSliceGroupIdc = changingSliceGroupIdc;

        return state;
    }
}
