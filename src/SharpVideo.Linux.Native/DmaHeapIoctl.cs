using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Specialized ioctl operations for DMA heap management.
/// </summary>
[SupportedOSPlatform("linux")]
public static class DmaHeapIoctl
{
    /// <summary>
    /// Allocates a DMA buffer from the heap.
    /// </summary>
    /// <param name="heapFd">File descriptor of the DMA heap</param>
    /// <param name="size">Size of the buffer to allocate</param>
    /// <param name="fdFlags">Flags for the returned file descriptor</param>
    /// <param name="heapFlags">Heap-specific allocation flags</param>
    /// <returns>Result containing the allocated buffer information</returns>
    public static (IoctlResult Result, DmaBuffer? Buffer) AllocateBuffer(
        int heapFd, 
        ulong size, 
        OpenFlags fdFlags = OpenFlags.O_RDWR | OpenFlags.O_CLOEXEC, 
        uint heapFlags = 0)
    {
        var allocationData = new DmaHeapAllocationData
        {
            len = size,
            fd_flags = (uint)fdFlags,
            heap_flags = heapFlags
        };

        var result = IoctlHelper.Ioctl(heapFd, IoctlConstants.DMA_HEAP_IOCTL_ALLOC, ref allocationData);
        
        if (!result.Success)
        {
            return (result, null);
        }

        var buffer = new DmaBuffer((int)allocationData.fd, size);

        return (result, buffer);
    }

    /// <summary>
    /// Allocates a DMA buffer with automatic error handling and logging.
    /// </summary>
    /// <param name="heapFd">File descriptor of the DMA heap</param>
    /// <param name="size">Size of the buffer to allocate</param>
    /// <param name="fdFlags">Flags for the returned file descriptor</param>
    /// <param name="heapFlags">Heap-specific allocation flags</param>
    /// <returns>The allocated buffer, or null if allocation failed</returns>
    public static DmaBuffer? TryAllocateBuffer(
        int heapFd, 
        ulong size, 
        OpenFlags fdFlags = OpenFlags.O_RDWR | OpenFlags.O_CLOEXEC, 
        uint heapFlags = 0)
    {
        var (result, buffer) = AllocateBuffer(heapFd, size, fdFlags, heapFlags);
        
        if (!result.Success)
        {
            // Could add logging here if needed
            return null;
        }

        return buffer;
    }

    /// <summary>
    /// Validates DMA heap allocation parameters.
    /// </summary>
    /// <param name="size">Buffer size to validate</param>
    /// <param name="fdFlags">File descriptor flags to validate</param>
    /// <returns>True if parameters are valid</returns>
    public static bool ValidateAllocationParameters(ulong size, OpenFlags fdFlags)
    {
        // Check for reasonable size limits
        if (size == 0 || size > (1UL << 32)) // 4GB limit
        {
            return false;
        }

        // Ensure required flags are present - DMA buffers need write access, so reject O_RDONLY
        if (!fdFlags.HasFlag(OpenFlags.O_RDWR) && !fdFlags.HasFlag(OpenFlags.O_WRONLY))
        {
            return false;
        }

        return true;
    }
}