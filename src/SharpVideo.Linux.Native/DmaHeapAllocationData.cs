using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>dma_heap_allocation_data</c> structure.
/// Used for passing metadata from userspace for DMA buffer allocations.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DmaHeapAllocationData
{
    /// <summary>
    /// Length of the allocation in bytes.
    /// </summary>
    public ulong len;

    /// <summary>
    /// File descriptor of the allocated DMA buffer (populated by the kernel).
    /// </summary>
    public uint fd;

    /// <summary>
    /// File descriptor flags for the allocation.
    /// </summary>
    public uint fd_flags;

    /// <summary>
    /// Heap-specific flags for the allocation. Currently no valid flags are defined.
    /// </summary>
    public ulong heap_flags;
}