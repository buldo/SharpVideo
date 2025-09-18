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
    public void TestIoctlConstants_DmaHeapConstantMatchesNative()
    {
        // Test that C# DMA_HEAP_IOCTL_ALLOC constant matches the real Linux kernel constant
        uint csharpConstant = IoctlConstants.DMA_HEAP_IOCTL_ALLOC;
        uint nativeConstant = NativeTestLibrary.GetNativeDmaHeapIoctlAlloc();

        // Debug information to help understand the mismatch
        int nativeSize = NativeTestLibrary.GetNativeDmaHeapAllocationDataSize();
        uint expectedConstant = IoctlConstants.IOWR(IoctlConstants.DMA_HEAP_IOC_MAGIC, 0, (uint)nativeSize);

        // Provide helpful error message with debug information
        if (csharpConstant != nativeConstant)
        {
            string errorMessage = $"DMA_HEAP_IOCTL_ALLOC constant mismatch!\n" +
                                 $"C# constant: 0x{csharpConstant:X8} (using size 32)\n" +
                                 $"Native constant: 0x{nativeConstant:X8}\n" +
                                 $"Native structure size: {nativeSize}\n" +
                                 $"Expected constant with correct size: 0x{expectedConstant:X8}";
            Assert.Fail(errorMessage);
        }

        Assert.Equal(nativeConstant, csharpConstant);
    }

    [Fact]
    public void TestV4L2Constants_IoctlGeneration()
    {
        // Test V4L2 ioctl constant generation
        uint queryCap = V4L2Constants.VIDIOC_QUERYCAP;
        uint getFmt = V4L2Constants.VIDIOC_G_FMT;
        uint setFmt = V4L2Constants.VIDIOC_S_FMT;
        uint reqBufs = V4L2Constants.VIDIOC_REQBUFS;
        uint streamOn = V4L2Constants.VIDIOC_STREAMON;
        uint streamOff = V4L2Constants.VIDIOC_STREAMOFF;

        // Verify constants are generated correctly (non-zero values)
        Assert.NotEqual(0u, queryCap);
        Assert.NotEqual(0u, getFmt);
        Assert.NotEqual(0u, setFmt);
        Assert.NotEqual(0u, reqBufs);
        Assert.NotEqual(0u, streamOn);
        Assert.NotEqual(0u, streamOff);

        // Verify they are all different
        uint[] constants = { queryCap, getFmt, setFmt, reqBufs, streamOn, streamOff };
        for (int i = 0; i < constants.Length; i++)
        {
            for (int j = i + 1; j < constants.Length; j++)
            {
                Assert.NotEqual(constants[i], constants[j]);
            }
        }
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

    [Fact]
    public void TestV4L2_WithMockDevice()
    {
        // Test V4L2 operations with /dev/null (will fail but test structure)
        int fd = Libc.open("/dev/null", OpenFlags.O_RDWR);

        if (fd < 0)
        {
            return; // Skip test
        }

        try
        {
            // Test query capabilities (will fail with /dev/null but tests the call structure)
            var result = LibV4L2.QueryCapabilities(fd, out var capability);

            // We expect this to fail with /dev/null, but the result should be valid
            Assert.False(result.Success);
            Assert.NotEqual(0, result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
            Assert.NotEmpty(result.ErrorMessage);

            // Test format operations
            var format = new V4L2Format
            {
                Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE
            };

            var formatResult = LibV4L2.GetFormat(fd, ref format);
            Assert.False(formatResult.Success); // Expected to fail with /dev/null

            // Test stream control operations
            var streamOnResult = LibV4L2.StreamOn(fd, V4L2BufferType.VIDEO_CAPTURE_MPLANE);
            Assert.False(streamOnResult.Success); // Expected to fail with /dev/null

            var streamOffResult = LibV4L2.StreamOff(fd, V4L2BufferType.VIDEO_CAPTURE_MPLANE);
            Assert.False(streamOffResult.Success); // Expected to fail with /dev/null
        }
        finally
        {
            Libc.close(fd);
        }
    }

    [Fact]
    public void TestV4L2_HelperMethods()
    {
        // Test V4L2 helper methods with /dev/null (will fail but test structure)
        int fd = Libc.open("/dev/null", OpenFlags.O_RDWR);

        if (fd < 0)
        {
            return; // Skip test
        }

        try
        {
            // Test multiplanar format helper
            var (formatResult, format) = LibV4L2.SetMultiplanarCaptureFormat(
                fd, 1920, 1080, V4L2PixelFormats.NV12M, 2);

            Assert.False(formatResult.Success); // Expected to fail with /dev/null

            // Verify format structure was set up correctly
            Assert.Equal(V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE, format.Type);
            Assert.Equal(1920u, format.Pix_mp.Width);
            Assert.Equal(1080u, format.Pix_mp.Height);
            Assert.Equal(V4L2PixelFormats.NV12M, format.Pix_mp.PixelFormat);
            Assert.Equal(2, format.Pix_mp.NumPlanes);

            // Test buffer request helper
            var (bufferResult, count) = LibV4L2.RequestMultiplanarDmaBufCapture(fd, 4);
            Assert.False(bufferResult.Success); // Expected to fail with /dev/null

            // Test decoder control helpers
            var startResult = LibV4L2.StartDecoder(fd);
            Assert.False(startResult.Success); // Expected to fail with /dev/null

            var stopResult = LibV4L2.StopDecoder(fd);
            Assert.False(stopResult.Success); // Expected to fail with /dev/null

            var flushResult = LibV4L2.FlushDecoder(fd);
            Assert.False(flushResult.Success); // Expected to fail with /dev/null
        }
        finally
        {
            Libc.close(fd);
        }
    }

    [Fact]
    public void TestV4L2_TryWithRealDevice()
    {
        // Try to find and test with a real V4L2 device (system-dependent)
        string[] devicePaths = {
            "/dev/video0",
            "/dev/video1",
            "/dev/video2",
            "/dev/video3"
        };

        bool foundDevice = false;
        foreach (var path in devicePaths)
        {
            int videoFd = Libc.open(path, OpenFlags.O_RDWR | OpenFlags.O_NONBLOCK, 0);
            if (videoFd >= 0)
            {
                foundDevice = true;
                try
                {
                    // Test query capabilities with real device
                    var result = LibV4L2.QueryCapabilities(videoFd, out var capability);
                    if (result.Success)
                    {
                        // Verify capability structure has reasonable values
                        Assert.NotEmpty(capability.DriverString);
                        Assert.NotEmpty(capability.CardString);
                        Assert.NotEqual(0u, capability.Version);
                        Assert.NotEqual(0u, capability.Capabilities);

                        // If device supports multiplanar capture, test format operations
                        if ((capability.DeviceCaps & (uint)V4L2Capabilities.VIDEO_CAPTURE_MPLANE) != 0)
                        {
                            var format = new V4L2Format
                            {
                                Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE
                            };

                            var formatResult = LibV4L2.GetFormat(videoFd, ref format);
                            // Note: We don't assert success here as it depends on device state
                            // but we verify the call structure works
                        }
                    }
                    // Note: We don't assert success here because device availability and
                    // functionality varies greatly between systems
                }
                finally
                {
                    Libc.close(videoFd);
                }
                break;
            }
        }

        // If no V4L2 device is available, the test passes (system-dependent)
        // This is expected behavior on systems without V4L2 devices
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