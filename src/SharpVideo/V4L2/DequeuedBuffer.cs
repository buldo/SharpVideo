using System.Runtime.Versioning;
using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

/// <summary>
/// Represents a dequeued buffer with metadata
/// </summary>
[SupportedOSPlatform("linux")]
public class DequeuedBuffer
{
    public uint Index { get; init; }
    public V4L2Plane[] Planes { get; init; } = Array.Empty<V4L2Plane>();
    public TimeVal Timestamp { get; init; }
    public uint Sequence { get; init; }
    public uint Flags { get; init; }

    public uint TotalBytesUsed
    {
        get
        {
            uint total = 0;
            foreach (var plane in Planes)
            {
                total += plane.BytesUsed;
            }
            return total;
        }
    }
}