using Xunit;
using SharpVideo.Linux.Native;

namespace SharpVideo.Linux.Native.Tests;

/// <summary>
/// Tests for ioctl functionality including basic operations, constants, and DMA heap operations
/// </summary>
public class IoctlTests
{
    [Fact]
    public void TestIoctlConstants_RequestCodeGeneration()
    {
        // Test request code generation
        uint ioRequest = IoctlConstants.IO((uint)'T', 1);
        uint iorRequest = IoctlConstants.IOR((uint)'T', 2, 4);
        uint iowRequest = IoctlConstants.IOW((uint)'T', 3, 8);
        uint iowrRequest = IoctlConstants.IOWR((uint)'T', 4, 16);
        
        // Verify the constants are generated correctly (non-zero values)
        Assert.NotEqual(0u, ioRequest);
        Assert.NotEqual(0u, iorRequest);
        Assert.NotEqual(0u, iowRequest);
        Assert.NotEqual(0u, iowrRequest);
        
        // Verify they are all different
        Assert.NotEqual(ioRequest, iorRequest);
        Assert.NotEqual(ioRequest, iowRequest);
        Assert.NotEqual(ioRequest, iowrRequest);
        Assert.NotEqual(iorRequest, iowRequest);
        Assert.NotEqual(iorRequest, iowrRequest);
        Assert.NotEqual(iowRequest, iowrRequest);
    }

    [Fact]
    public void TestIoctlConstants_DmaHeapConstant()
    {
        // Test DMA heap constant is defined
        uint dmaHeapAlloc = IoctlConstants.DMA_HEAP_IOCTL_ALLOC;
        Assert.NotEqual(0u, dmaHeapAlloc);
    }

    [Fact]
    public void TestBasicIoctl_WithDevNull()
    {
        // Test with /dev/null (should always be available)
        int fd = Libc.open("/dev/null", OpenFlags.O_RDWR);
        
        // Skip test if /dev/null is not available (unlikely but possible in some containers)
        if (fd < 0)
        {
            return; // Skip test
        }

        try
        {
            // This will likely fail, but tests the error handling
            uint request = IoctlConstants.IO((uint)'T', 1);
            var result = IoctlHelper.Ioctl(fd, request);
            
            // We expect this to fail, but the result should be valid
            Assert.False(result.Success);
            Assert.NotEqual(0, result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
            Assert.NotEmpty(result.ErrorMessage);
        }
        finally
        {
            Libc.close(fd);
        }
    }

    [Theory]
    [InlineData(1024ul, OpenFlags.O_RDWR, true)]
    [InlineData(0ul, OpenFlags.O_RDWR, false)]
    [InlineData(1024ul, (OpenFlags)0, false)]
    [InlineData(4096ul, OpenFlags.O_RDWR | OpenFlags.O_CLOEXEC, true)]
    public void TestDmaHeapIoctl_ParameterValidation(ulong size, OpenFlags flags, bool expectedValid)
    {
        bool result = DmaHeapIoctl.ValidateAllocationParameters(size, flags);
        Assert.Equal(expectedValid, result);
    }

    [Fact]
    public void TestDmaHeapIoctl_TryAllocateBuffer()
    {
        // Try to allocate from DMA heap (may not be available on all systems)
        string[] heapPaths = {
            "/dev/dma_heap/system",
            "/dev/dma_heap/linux,cma"
        };

        bool foundHeap = false;
        foreach (var path in heapPaths)
        {
            int heapFd = Libc.open(path, OpenFlags.O_RDWR | OpenFlags.O_CLOEXEC, 0);
            if (heapFd >= 0)
            {
                foundHeap = true;
                try
                {
                    var buffer = DmaHeapIoctl.TryAllocateBuffer(heapFd, 4096);
                    if (buffer.HasValue)
                    {
                        // Verify buffer properties
                        Assert.True(buffer.Value.Fd >= 0);
                        Assert.Equal(4096ul, buffer.Value.Size);
                        buffer.Value.Close();
                    }
                    // Note: We don't assert success here because DMA allocation may fail
                    // depending on system configuration and available memory
                }
                finally
                {
                    Libc.close(heapFd);
                }
                break;
            }
        }
        
        // If no DMA heap is available, the test passes (system-dependent)
        // This is expected behavior on systems without DMA heap support
    }

    [Fact]
    public void TestIoctlLogging_EnableDisable()
    {
        // Test that logging can be enabled and disabled without errors
        try
        {
            // Enable logging with a test logger
            var testLogger = new TestIoctlLogger();
            IoctlHelperWithLogging.SetLogger(testLogger);
            
            // Perform an ioctl operation that will be logged
            int fd = Libc.open("/dev/null", OpenFlags.O_RDWR);
            if (fd >= 0)
            {
                try
                {
                    uint request = IoctlConstants.IO((uint)'X', 99);
                    var result = IoctlHelperWithLogging.Ioctl(fd, request, "test operation");
                    
                    // Verify the operation was logged
                    Assert.True(testLogger.LoggedOperations.Count > 0);
                }
                finally
                {
                    Libc.close(fd);
                }
            }
            
            // Disable logging
            IoctlHelperWithLogging.SetLogger(null);
        }
        finally
        {
            // Ensure logging is disabled after test
            IoctlHelperWithLogging.SetLogger(null);
        }
    }
}

/// <summary>
/// Test implementation of IoctlLogger for testing purposes
/// </summary>
internal class TestIoctlLogger : IIoctlLogger
{
    public List<string> LoggedOperations { get; } = new List<string>();

    public void LogIoctlCall(int fd, uint request, string operation)
    {
        LoggedOperations.Add($"Call: fd={fd}, request=0x{request:X8}, op={operation}");
    }

    public void LogIoctlSuccess(int fd, uint request, string operation)
    {
        LoggedOperations.Add($"Success: fd={fd}, request=0x{request:X8}, op={operation}");
    }

    public void LogIoctlError(int fd, uint request, string operation, int errorCode, string errorMessage)
    {
        LoggedOperations.Add($"Error: fd={fd}, request=0x{request:X8}, op={operation}, error={errorCode} ({errorMessage})");
    }
}