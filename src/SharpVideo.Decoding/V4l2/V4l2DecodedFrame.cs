using System.Runtime.Versioning;

using SharpVideo.Drm;
using SharpVideo.Utils;

namespace SharpVideo.Decoding.V4l2;

/// <summary>
/// Represents a decoded frame from a V4L2 decoder.
/// Uses DMA-BUF for zero-copy buffer sharing.
/// </summary>
[SupportedOSPlatform("linux")]
public class V4l2DecodedFrame : UniversalDecodedFrame
{
    /// <summary>
    /// Creates a V4L2 decoded frame from a DMA buffer.
    /// </summary>
    public V4l2DecodedFrame(SharedDmaBuffer dmaBuffer)
    {
        DmaBuffer = dmaBuffer;
    }

    /// <summary>
    /// The DMA buffer containing the decoded frame data.
    /// </summary>
    public SharedDmaBuffer DmaBuffer { get; }

    /// <summary>
    /// Frame width in pixels.
    /// </summary>
    public uint Width => DmaBuffer.Width;

    /// <summary>
    /// Frame height in pixels.
    /// </summary>
    public uint Height => DmaBuffer.Height;

    /// <summary>
    /// Stride (bytes per line) of the frame.
    /// </summary>
    public uint Stride => DmaBuffer.Stride;

    /// <summary>
    /// Pixel format of the decoded frame.
    /// </summary>
    public PixelFormat Format => DmaBuffer.Format;

    /// <summary>
    /// Gets the buffer index for requeuing to the decoder.
    /// </summary>
    public uint BufferIndex => DmaBuffer.V4L2Buffer.Index;
}
