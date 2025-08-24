using SharpVideo.Linux.Native;

namespace SharpVideo.DmaBuffers;

public class DmaBuffersAllocator
{
    private readonly int _fd;
    private readonly Dictionary<int, DmaBuffer> _allocatedBuffers = new();

    private DmaBuffersAllocator(int heapFd)
    {
        _fd = heapFd;
    }

    public static bool TryCreate(out DmaBuffersAllocator? buffer)
    {
        string[] paths = {
            "/dev/dma_heap/vidbuf_cached",
            "/dev/dma_heap/linux,cma",
            "/dev/dma_heap/system" // Добавляем системный heap на случай, если другие недоступны
        };

        foreach (var path in paths)
        {
            var heapFd = Libc.open(path, OpenFlags.O_RDWR | OpenFlags.O_CLOEXEC, 0);

            if (heapFd >= 0)
            {
                buffer = new DmaBuffersAllocator(heapFd);
                return true;
            }

            //Console.WriteLine($"Failed to open DMA heap: {path}, errno: {Marshal.GetLastWin32Error()}");
        }

        buffer = null;
        return false;
    }

    public DmaBuffer? Allocate(ulong size)
    {
        var allocationData = new dma_heap_allocation_data
        {
            len = size,
            fd_flags = (uint)(OpenFlags.O_RDWR | OpenFlags.O_CLOEXEC),
            heap_flags = 0 // Нет специальных флагов
        };

        unsafe
        {
            if (Libc.ioctl(_fd, DMA_HEAP_IOCTL_ALLOC, (IntPtr)(&allocationData)) != 0)
            {
                //Console.WriteLine($"❌ DMA-BUF allocation failed, errno: {Marshal.GetLastWin32Error()}");
                return null;
            }
        }

        var bufInfo = new DmaBuffer
        {
            Fd = (int)allocationData.fd,
            Size = size
        };

        _allocatedBuffers.Add(bufInfo.Fd, bufInfo);

        return bufInfo;
    }
}