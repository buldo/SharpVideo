using System.Runtime.Versioning;

using SharpVideo.V4L2;

namespace SharpVideo.Decoding.V4l2;

/// <summary>
/// Encoded buffer for V4L2 decoder input containing H264 NAL units.
/// Wraps a V4L2MMapMPlaneBuffer for zero-copy data transfer to the decoder.
/// </summary>
[SupportedOSPlatform("linux")]
public class V4l2EncodedBuffer : UniversalEncodedBuffer
{
    private readonly V4L2MMapMPlaneBuffer _mmapBuffer;
    private int _usedBytes;
    private int _naluPayloadStart;

    /// <summary>
    /// Creates a new V4L2 encoded buffer wrapping the specified mmap buffer.
    /// </summary>
    /// <param name="mmapBuffer">The underlying V4L2 memory-mapped buffer.</param>
    public V4l2EncodedBuffer(V4L2MMapMPlaneBuffer mmapBuffer)
        : base((int)mmapBuffer.Planes[0].Length)
    {
        _mmapBuffer = mmapBuffer;
    }

    /// <summary>
    /// The underlying V4L2 memory-mapped buffer.
    /// </summary>
    public V4L2MMapMPlaneBuffer MMapBuffer => _mmapBuffer;

    /// <summary>
    /// Copies data from a span into the buffer.
    /// Supports 3 or 4 byte start codes.
    /// </summary>
    public override void CopyFromSpan(ReadOnlySpan<byte> nalu)
    {
        _mmapBuffer.CopyDataToPlane(nalu, 0);
        _usedBytes = nalu.Length;
        _naluPayloadStart = GetPayloadStart(nalu);
    }

    /// <summary>
    /// Gets the currently used portion of the buffer.
    /// </summary>
    public ReadOnlySpan<byte> GetData() => _mmapBuffer.MappedPlanes[0].AsSpan().Slice(0, _usedBytes);

    /// <summary>
    /// Gets the NALU payload (data after start code).
    /// </summary>
    public ReadOnlySpan<byte> GetPayload() => _mmapBuffer.MappedPlanes[0].AsSpan().Slice(_naluPayloadStart, _usedBytes - _naluPayloadStart);

    /// <summary>
    /// Resets the buffer for reuse.
    /// </summary>
    public void Reset()
    {
        _usedBytes = 0;
        _naluPayloadStart = 0;
    }
}
