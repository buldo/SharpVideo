using System.Runtime.Versioning;

using SharpVideo.Linux.Native;

namespace SharpVideo.DmaBuffers;

/// <summary>
/// Represents a DMA buffer allocated from a DMA heap.
/// </summary>
[SupportedOSPlatform("linux")]
public class DmaBuffer
{
    /// <summary>
    /// File descriptor for the DMA buffer.
    /// </summary>
    public required int Fd { get; init; }

    /// <summary>
    /// Size of the DMA buffer in bytes.
    /// </summary>
    public required nuint Size { get; init; }

    /// <summary>
    /// Indicates whether the buffer has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    public MapStatus MapStatus { get; private set; } = MapStatus.NotMapped;


    public void MapBuffer()
    {
        // Map the buffer to fill it
        var map = Libc.mmap(
            IntPtr.Zero,
            Size,
            ProtFlags.PROT_READ | ProtFlags.PROT_WRITE,
            MapFlags.MAP_SHARED,
            Fd,
            0);

        if (map == Libc.MAP_FAILED)
        {
            MapStatus = MapStatus.FailedToMap;
            return;
        }
    }

    /// <summary>
    /// Closes the DMA buffer file descriptor and marks it as disposed.
    /// </summary>
    public void Dispose()
    {
        if (!IsDisposed && Fd >= 0)
        {
            Linux.Native.Libc.close(Fd);
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

public enum MapStatus
{
    NotMapped,
    FailedToMap,
    Mapped
}