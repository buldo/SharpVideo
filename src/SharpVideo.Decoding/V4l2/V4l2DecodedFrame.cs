using System.Runtime.Versioning;

using SharpVideo.Drm;
using SharpVideo.Utils;
using SharpVideo.V4L2;

namespace SharpVideo.Decoding.V4l2;

/// <summary>
/// Represents a decoded frame from a V4L2 decoder.
/// Supports both DMA-BUF (zero-copy) and MMAP buffer modes.
/// </summary>
[SupportedOSPlatform("linux")]
public class V4l2DecodedFrame : UniversalDecodedFrame
{
    /// <summary>
    /// Creates a V4L2 decoded frame from a DMA buffer (zero-copy mode).
    /// </summary>
    public V4l2DecodedFrame(SharedDmaBuffer dmaBuffer)
    {
        DmaBuffer = dmaBuffer;
        Width = dmaBuffer.Width;
        Height = dmaBuffer.Height;
        Stride = dmaBuffer.Stride;
        Format = dmaBuffer.Format;
        IsDmaBuf = true;
    }

    /// <summary>
    /// Creates a V4L2 decoded frame from MMAP buffer data.
    /// </summary>
    public V4l2DecodedFrame(
        V4L2MMapMPlaneBuffer mmapBuffer,
        uint width,
        uint height,
        uint stride,
        PixelFormat format)
    {
        MmapBuffer = mmapBuffer;
        Width = width;
        Height = height;
        Stride = stride;
        Format = format;
        IsDmaBuf = false;
    }

    /// <summary>
    /// True if this frame uses DMA-BUF (zero-copy), false if MMAP.
    /// </summary>
    public bool IsDmaBuf { get; }

    /// <summary>
    /// The DMA buffer (only valid if <see cref="IsDmaBuf"/> is true).
    /// </summary>
    public SharedDmaBuffer? DmaBuffer { get; }

    /// <summary>
    /// The MMAP buffer (only valid if <see cref="IsDmaBuf"/> is false).
    /// </summary>
    public V4L2MMapMPlaneBuffer? MmapBuffer { get; }

    /// <summary>
    /// Frame width in pixels.
    /// </summary>
    public uint Width { get; }

    /// <summary>
    /// Frame height in pixels.
    /// </summary>
    public uint Height { get; }

    /// <summary>
    /// Stride (bytes per line) of the frame.
    /// </summary>
    public uint Stride { get; }

    /// <summary>
    /// Pixel format of the decoded frame.
    /// </summary>
    public PixelFormat Format { get; }

    /// <summary>
    /// Gets the buffer index for requeuing to the decoder.
    /// </summary>
    public uint BufferIndex
    {
        get
        {
            if (IsDmaBuf && DmaBuffer is not null)
            {
                return DmaBuffer.V4L2Buffer.Index;
            }

            if (MmapBuffer is not null)
            {
                return MmapBuffer.Index;
            }

            throw new InvalidOperationException("No valid buffer associated with this frame");
        }
    }

    /// <summary>
    /// Gets the frame data as a span (for MMAP mode).
    /// </summary>
    public ReadOnlySpan<byte> GetData()
    {
        if (IsDmaBuf)
        {
            throw new InvalidOperationException("Cannot get data span for DMA-BUF frames. Use DmaBuffer.DmaBuffer directly.");
        }

        if (MmapBuffer is null)
        {
            throw new InvalidOperationException("MMAP buffer is null");
        }

        return MmapBuffer.MappedPlanes[0].AsSpan();
    }
}
