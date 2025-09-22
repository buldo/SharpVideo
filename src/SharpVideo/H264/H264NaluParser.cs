using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpVideo.H264;

public static class H264NaluParser
{
    public static NaluType GetNaluType(ReadOnlySpan<byte> nalu)
    {
        return NaluType.zero;
    }
}

public enum NaluType
{

}