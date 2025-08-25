namespace SharpVideo.DmaBuffers;

/// <summary>
/// Represents a DMA buffer allocated from a DMA heap.
/// </summary>
public class DmaBuffer
{
    /// <summary>
    /// File descriptor for the DMA buffer.
    /// </summary>
    public required int Fd { get; init; }

    /// <summary>
    /// Size of the DMA buffer in bytes.
    /// </summary>
    public required ulong Size { get; init; }

    /// <summary>
    /// Indicates whether the buffer has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Closes the DMA buffer file descriptor and marks it as disposed.
    /// </summary>
    public void Dispose()
    {
        if (!IsDisposed && Fd >= 0)
        {
            SharpVideo.Linux.Native.Libc.close(Fd);
            IsDisposed = true;
        }
    }

    /// <summary>
    /// Finalizer to ensure the file descriptor is closed.
    /// </summary>
    ~DmaBuffer()
    {
        Dispose();
    }
}