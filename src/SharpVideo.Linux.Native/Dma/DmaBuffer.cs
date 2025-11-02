using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native.Dma;

/// <summary>
/// Represents a DMA buffer allocated from a DMA heap (native version).
/// </summary>
[SupportedOSPlatform("linux")]
public readonly struct DmaBuffer
{
    /// <summary>
    /// File descriptor for the DMA buffer.
    /// </summary>
    public int Fd { get; }

    /// <summary>
    /// Size of the DMA buffer in bytes.
    /// </summary>
    public ulong Size { get; }

    /// <summary>
    /// Creates a new DMA buffer instance.
    /// </summary>
    /// <param name="fd">File descriptor</param>
    /// <param name="size">Buffer size in bytes</param>
    public DmaBuffer(int fd, ulong size)
    {
        Fd = fd;
        Size = size;
    }

    /// <summary>
    /// Closes the DMA buffer file descriptor.
    /// </summary>
    public void Close()
    {
        if (Fd >= 0)
        {
            Libc.close(Fd);
        }
    }
}