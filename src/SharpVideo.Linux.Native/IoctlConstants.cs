using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Constants and utilities for ioctl operations.
/// </summary>
[SupportedOSPlatform("linux")]
public static class IoctlConstants
{
    // ioctl direction bits
    public const uint IOC_NONE = 0U;
    public const uint IOC_WRITE = 1U;
    public const uint IOC_READ = 2U;

    // ioctl size and direction encoding
    public const int IOC_NRBITS = 8;
    public const int IOC_TYPEBITS = 8;
    public const int IOC_SIZEBITS = 14;
    public const int IOC_DIRBITS = 2;

    public const int IOC_NRMASK = (1 << IOC_NRBITS) - 1;
    public const int IOC_TYPEMASK = (1 << IOC_TYPEBITS) - 1;
    public const int IOC_SIZEMASK = (1 << IOC_SIZEBITS) - 1;
    public const int IOC_DIRMASK = (1 << IOC_DIRBITS) - 1;

    public const int IOC_NRSHIFT = 0;
    public const int IOC_TYPESHIFT = IOC_NRSHIFT + IOC_NRBITS;
    public const int IOC_SIZESHIFT = IOC_TYPESHIFT + IOC_TYPEBITS;
    public const int IOC_DIRSHIFT = IOC_SIZESHIFT + IOC_SIZEBITS;

    /// <summary>
    /// Creates an ioctl request code.
    /// </summary>
    /// <param name="dir">Direction (IOC_NONE, IOC_READ, IOC_WRITE, or IOC_READ|IOC_WRITE)</param>
    /// <param name="type">Device type (usually a character)</param>
    /// <param name="nr">Request number</param>
    /// <param name="size">Size of the data structure</param>
    /// <returns>The ioctl request code</returns>
    public static uint IOC(uint dir, uint type, uint nr, uint size)
    {
        return (dir << IOC_DIRSHIFT) |
               (type << IOC_TYPESHIFT) |
               (nr << IOC_NRSHIFT) |
               (size << IOC_SIZESHIFT);
    }

    /// <summary>
    /// Creates an ioctl request code for operations with no data transfer.
    /// </summary>
    /// <param name="type">Device type</param>
    /// <param name="nr">Request number</param>
    /// <returns>The ioctl request code</returns>
    public static uint IO(uint type, uint nr) => IOC(IOC_NONE, type, nr, 0);

    /// <summary>
    /// Creates an ioctl request code for read operations.
    /// </summary>
    /// <param name="type">Device type</param>
    /// <param name="nr">Request number</param>
    /// <param name="size">Size of the data structure</param>
    /// <returns>The ioctl request code</returns>
    public static uint IOR(uint type, uint nr, uint size) => IOC(IOC_READ, type, nr, size);

    /// <summary>
    /// Creates an ioctl request code for write operations.
    /// </summary>
    /// <param name="type">Device type</param>
    /// <param name="nr">Request number</param>
    /// <param name="size">Size of the data structure</param>
    /// <returns>The ioctl request code</returns>
    public static uint IOW(uint type, uint nr, uint size) => IOC(IOC_WRITE, type, nr, size);

    /// <summary>
    /// Creates an ioctl request code for read/write operations.
    /// </summary>
    /// <param name="type">Device type</param>
    /// <param name="nr">Request number</param>
    /// <param name="size">Size of the data structure</param>
    /// <returns>The ioctl request code</returns>
    public static uint IOWR(uint type, uint nr, uint size) => IOC(IOC_READ | IOC_WRITE, type, nr, size);

    // DMA Heap ioctl constants
    public const uint DMA_HEAP_IOC_MAGIC = (uint)'H';
    public static readonly uint DMA_HEAP_IOCTL_ALLOC = IOWR(DMA_HEAP_IOC_MAGIC, 0, 32); // sizeof(DmaHeapAllocationData)

    // DRM ioctl constants
    public const uint DRM_IOCTL_BASE = (uint)'d';
    public static uint DRM_IO(uint nr) => IO(DRM_IOCTL_BASE, nr);
    public static uint DRM_IOR(uint nr, uint size) => IOR(DRM_IOCTL_BASE, nr, size);
    public static uint DRM_IOW(uint nr, uint size) => IOW(DRM_IOCTL_BASE, nr, size);
    public static uint DRM_IOWR(uint nr, uint size) => IOWR(DRM_IOCTL_BASE, nr, size);
}