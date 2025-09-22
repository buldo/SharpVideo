using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpVideo.H264;

public class H264NaluParser
{
    public H264NaluParser(NaluMode naluMode)
    {

    }

    public H264NaluType GetNaluType(ReadOnlySpan<byte> nalu)
    {
        if (nalu.Length == 0)
            return H264NaluType.Unspecified;

        // Extract NALU type from the first byte (bits 0-4)
        var naluHeader = nalu[0];
        var naluTypeValue = naluHeader & 0x1F;

        return (H264NaluType)naluTypeValue;
    }
}