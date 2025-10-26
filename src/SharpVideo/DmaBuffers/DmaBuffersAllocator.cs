using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;

using SharpVideo.Linux.Native;

using static SharpVideo.Linux.Native.DmaHeapIoctl;

namespace SharpVideo.DmaBuffers;

[SupportedOSPlatform("linux")]
public class DmaBuffersAllocator
{
    private readonly int _fd;
    private readonly Dictionary<int, DmaBuffer> _allocatedBuffers = new();

    private DmaBuffersAllocator(int heapFd)
    {
        _fd = heapFd;
    }

    public static bool TryCreate([NotNullWhen(true)] out DmaBuffersAllocator? allocator)
    {
        string[] paths =
        {
            "/dev/dma_heap/reserved", // Reserved memory heap (often used for video HW)
            "/dev/dma_heap/vidbuf_cached",
            "/dev/dma_heap/linux,cma",
            "/dev/dma_heap/system" // System heap as fallback
        };

        foreach (var path in paths)
        {
            var heapFd = Libc.open(path, OpenFlags.O_RDWR | OpenFlags.O_CLOEXEC, 0);

            if (heapFd >= 0)
            {
                Console.WriteLine($"Successfully opened DMA heap: {path}");
                allocator = new DmaBuffersAllocator(heapFd);
                return true;
            }

            //Console.WriteLine($"Failed to open DMA heap: {path}, errno: {Marshal.GetLastWin32Error()}");
        }

        allocator = null;
        return false;
    }

    public static DmaBuffersAllocator Create()
    {
        if (TryCreate(out var allocator))
        {
            return allocator;
        }

        throw new Exception("Failed to create DmaBuffersAllocator");
    }

    public DmaBuffer? Allocate(ulong size)
    {
        // Use the new ioctl system for allocation
        var nativeBuffer = TryAllocateBuffer(_fd, size);

        if (nativeBuffer != null)
        {
            var buffer = new DmaBuffer
            {
                Fd = nativeBuffer.Value.Fd,
                Size = (UIntPtr)nativeBuffer.Value.Size
            };
            _allocatedBuffers.Add(buffer.Fd, buffer);
            return buffer;
        }

        return null;
    }
}