using System.Runtime.Versioning;

using SharpVideo.Linux.Native.C;
using SharpVideo.Linux.Native.V4L2;

namespace SharpVideo.V4L2;

/// <summary>
/// Represents a dequeued buffer with metadata
/// </summary>
[SupportedOSPlatform("linux")]
public class DequeuedBuffer
{
    public uint Index { get; init; }
    public V4L2Plane[] Planes { get; init; } = Array.Empty<V4L2Plane>();

    /// <summary>
    /// The timestamp from the V4L2 buffer.
    /// For stateless decoders, this identifies which frame was decoded.
    /// The timestamp is set on the OUTPUT buffer and copied by the driver to the CAPTURE buffer.
    /// </summary>
    public TimeVal Timestamp { get; init; }

    /// <summary>
    /// Gets the frame number from the timestamp.
    /// Following GStreamer convention: frame_num = tv_sec * 1_000_000 + tv_usec
    /// </summary>
    public uint FrameNumber => (uint)((ulong)Timestamp.TvSec * 1_000_000 + (ulong)Timestamp.TvUsec);

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