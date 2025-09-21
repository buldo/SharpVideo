using System;
using Xunit;

namespace SharpVideo.Linux.Native.Tests;

/// <summary>
/// Tests for utility helper methods and classes
/// </summary>
public class UtilityMethodsTests
{
    [Fact]
    public void TestFourCC_UtilityFunction()
    {
        // Test that FourCC utility function works correctly
        uint testFourCC = FourCC.FromChars('T', 'E', 'S', 'T');

        // Verify the FOURCC is constructed correctly (little-endian)
        Assert.NotEqual(0u, testFourCC);

        // Test with known values
        uint nv12 = FourCC.FromChars('N', 'M', '1', '2');
        Assert.Equal(V4L2PixelFormats.NV12M, nv12);

        uint yuyv = FourCC.FromChars('Y', 'U', 'Y', 'V');
        Assert.Equal(V4L2PixelFormats.YUYV, yuyv);
    }

    [Fact]
    public void TestV4L2PixelFormats_CommonFormats()
    {
        // Test that common pixel format constants are defined and have valid FOURCC values
        Assert.NotEqual(0u, V4L2PixelFormats.NV12M);
        Assert.NotEqual(0u, V4L2PixelFormats.NV21M);
        Assert.NotEqual(0u, V4L2PixelFormats.NV12M);
        Assert.NotEqual(0u, V4L2PixelFormats.NV21M);
        Assert.NotEqual(0u, V4L2PixelFormats.YUV420);
        Assert.NotEqual(0u, V4L2PixelFormats.YVU420);
        Assert.NotEqual(0u, V4L2PixelFormats.YUYV);
        Assert.NotEqual(0u, V4L2PixelFormats.UYVY);
        Assert.NotEqual(0u, V4L2PixelFormats.RGB565);
        Assert.NotEqual(0u, V4L2PixelFormats.RGB24);
        Assert.NotEqual(0u, V4L2PixelFormats.BGR24);
        Assert.NotEqual(0u, V4L2PixelFormats.RGB32);
        Assert.NotEqual(0u, V4L2PixelFormats.BGR32);
        Assert.NotEqual(0u, V4L2PixelFormats.ARGB32);
        Assert.NotEqual(0u, V4L2PixelFormats.ABGR32);

        // Test that different formats have different values
        Assert.NotEqual(V4L2PixelFormats.NV12M, V4L2PixelFormats.NV21M);
        Assert.NotEqual(V4L2PixelFormats.NV12M, V4L2PixelFormats.NV21M);
        Assert.NotEqual(V4L2PixelFormats.RGB24, V4L2PixelFormats.BGR24);
        Assert.NotEqual(V4L2PixelFormats.RGB32, V4L2PixelFormats.BGR32);
        Assert.NotEqual(V4L2PixelFormats.YUYV, V4L2PixelFormats.UYVY);
    }

    [Fact]
    public void TestV4L2PixelFormats_H264Format()
    {
        // Test H.264 format constant
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_H264);
        Assert.NotEqual(V4L2PixelFormats.V4L2_PIX_FMT_H264, V4L2PixelFormats.NV12M);
    }

    [Fact]
    public void TestIoctlConstants_UtilityFunctions()
    {
        // Test that ioctl utility functions generate valid request codes
        uint ioRequest = IoctlConstants.IO(100, 1);
        uint iorRequest = IoctlConstants.IOR(100, 2, 4);
        uint iowRequest = IoctlConstants.IOW(100, 3, 8);
        uint iowrRequest = IoctlConstants.IOWR(100, 4, 16);

        // Verify that different types generate different values
        Assert.NotEqual(ioRequest, iorRequest);
        Assert.NotEqual(ioRequest, iowRequest);
        Assert.NotEqual(ioRequest, iowrRequest);
        Assert.NotEqual(iorRequest, iowRequest);
        Assert.NotEqual(iorRequest, iowrRequest);
        Assert.NotEqual(iowRequest, iowrRequest);

        // Verify that all generated values are non-zero
        Assert.NotEqual(0u, ioRequest);
        Assert.NotEqual(0u, iorRequest);
        Assert.NotEqual(0u, iowRequest);
        Assert.NotEqual(0u, iowrRequest);
    }

    [Fact]
    public void TestIoctlConstants_DmaHeapConstant()
    {
        // Test DMA heap constant is defined
        uint dmaHeapAlloc = IoctlConstants.DMA_HEAP_IOCTL_ALLOC;
        Assert.NotEqual(0u, dmaHeapAlloc);
    }

    [Fact]
    public void TestIoctlConstants_BasicStructure()
    {
        // Test basic structure of ioctl constants
        uint magic = 100;
        uint number = 5;
        uint size = 16;

        // Test that same parameters generate same values
        uint iow1 = IoctlConstants.IOW(magic, number, size);
        uint iow2 = IoctlConstants.IOW(magic, number, size);
        Assert.Equal(iow1, iow2);

        // Test that different parameters generate different values
        uint differentMagic = IoctlConstants.IOW(101, number, size);
        uint differentNumber = IoctlConstants.IOW(magic, 6, size);
        uint differentSize = IoctlConstants.IOW(magic, number, 32);

        Assert.NotEqual(iow1, differentMagic);
        Assert.NotEqual(iow1, differentNumber);
        Assert.NotEqual(iow1, differentSize);
    }

    [Fact]
    public void TestDmaHeapIoctl_ValidateAllocationParameters()
    {
        // Test parameter validation for DMA heap allocations
        Assert.True(DmaHeapIoctl.ValidateAllocationParameters(4096, OpenFlags.O_RDWR));
        Assert.True(DmaHeapIoctl.ValidateAllocationParameters(1024, OpenFlags.O_RDWR | OpenFlags.O_CLOEXEC));

        // Test invalid parameters
        Assert.False(DmaHeapIoctl.ValidateAllocationParameters(0, OpenFlags.O_RDWR)); // Zero size
        Assert.False(DmaHeapIoctl.ValidateAllocationParameters(4096, (OpenFlags)0)); // No flags
    }

    [Fact]
    public void TestV4L2Constants_BufferTypes()
    {
        // Test that V4L2 buffer type constants have expected values
        Assert.Equal(1u, V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE);
        Assert.Equal(2u, V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT);
        Assert.Equal(9u, V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE);
        Assert.Equal(10u, V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE);
        Assert.Equal(13u, V4L2Constants.V4L2_BUF_TYPE_META_CAPTURE);
        Assert.Equal(14u, V4L2Constants.V4L2_BUF_TYPE_META_OUTPUT);
    }

    [Fact]
    public void TestV4L2Constants_MemoryTypes()
    {
        // Test that V4L2 memory type constants have expected values
        Assert.Equal(1u, V4L2Constants.V4L2_MEMORY_MMAP);
        Assert.Equal(2u, V4L2Constants.V4L2_MEMORY_USERPTR);
        Assert.Equal(3u, V4L2Constants.V4L2_MEMORY_OVERLAY);
        Assert.Equal(4u, V4L2Constants.V4L2_MEMORY_DMABUF);
    }

    [Fact]
    public void TestV4L2Constants_IoctlMagic()
    {
        // Test that V4L2 ioctl magic number is correct
        Assert.Equal((uint)'V', V4L2Constants.V4L2_IOCTL_MAGIC);
    }

    [Fact]
    public void TestV4L2Constants_CommonIoctls()
    {
        // Test that common V4L2 ioctl constants are defined and non-zero
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_QUERYCAP);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_FMT);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_S_FMT);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_REQBUFS);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_STREAMON);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_STREAMOFF);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_QBUF);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_DQBUF);

        // Verify they are all different
        var ioctls = new uint[] {
            V4L2Constants.VIDIOC_QUERYCAP,
            V4L2Constants.VIDIOC_G_FMT,
            V4L2Constants.VIDIOC_S_FMT,
            V4L2Constants.VIDIOC_REQBUFS,
            V4L2Constants.VIDIOC_STREAMON,
            V4L2Constants.VIDIOC_STREAMOFF,
            V4L2Constants.VIDIOC_QBUF,
            V4L2Constants.VIDIOC_DQBUF
        };

        for (int i = 0; i < ioctls.Length; i++)
        {
            for (int j = i + 1; j < ioctls.Length; j++)
            {
                Assert.NotEqual(ioctls[i], ioctls[j]);
            }
        }
    }

    [Fact]
    public void TestV4L2Constants_ControlConstants()
    {
        // Test V4L2 control constants
        Assert.NotEqual(0u, V4L2Constants.V4L2_CTRL_CLASS_USER);
        Assert.NotEqual(0u, V4L2Constants.V4L2_CTRL_CLASS_CODEC);
        Assert.NotEqual(0u, V4L2Constants.V4L2_CID_CODEC_BASE);

        // Test that different control classes have different values
        Assert.NotEqual(V4L2Constants.V4L2_CTRL_CLASS_USER, V4L2Constants.V4L2_CTRL_CLASS_CODEC);
    }

    [Fact]
    public void TestV4L2Constants_OtherConstants()
    {
        // Test other V4L2 constants
        Assert.Equal(32u, V4L2Constants.VIDEO_MAX_FRAME);
        Assert.Equal(8u, V4L2Constants.VIDEO_MAX_PLANES);
    }

    [Theory]
    [InlineData('A', 'B', 'C', 'D')]
    [InlineData('N', 'V', '1', '2')]
    [InlineData('Y', 'U', 'Y', 'V')]
    [InlineData('H', '2', '6', '4')]
    public void TestFourCC_VariousInputs(char a, char b, char c, char d)
    {
        // Test FourCC with various inputs
        uint fourcc = FourCC.FromChars(a, b, c, d);
        Assert.NotEqual(0u, fourcc);

        // Test consistency - same input should produce same output
        uint fourcc2 = FourCC.FromChars(a, b, c, d);
        Assert.Equal(fourcc, fourcc2);
    }

    [Fact]
    public void TestV4L2PixelFormats_Planar_vs_Packed()
    {
        // Test that planar and packed formats are different
        Assert.NotEqual(V4L2PixelFormats.YUV420, V4L2PixelFormats.YUV420M); // Packed vs Multi-planar
        Assert.NotEqual(V4L2PixelFormats.YVU420, V4L2PixelFormats.YVU420M); // Packed vs Multi-planar
        Assert.NotEqual(V4L2PixelFormats.YUV422P, V4L2PixelFormats.YUV422M); // Packed vs Multi-planar
        Assert.NotEqual(V4L2PixelFormats.YUV444P, V4L2PixelFormats.YUV444M); // Packed vs Multi-planar
    }

    [Fact]
    public void TestV4L2PixelFormats_RGBFormats()
    {
        // Test RGB format variations
        Assert.NotEqual(V4L2PixelFormats.RGB332, V4L2PixelFormats.RGB444);
        Assert.NotEqual(V4L2PixelFormats.RGB444, V4L2PixelFormats.RGB555);
        Assert.NotEqual(V4L2PixelFormats.RGB555, V4L2PixelFormats.RGB565);
        Assert.NotEqual(V4L2PixelFormats.RGB565, V4L2PixelFormats.RGB24);
        Assert.NotEqual(V4L2PixelFormats.RGB24, V4L2PixelFormats.RGB32);

        // Test that RGB and BGR variants are different
        Assert.NotEqual(V4L2PixelFormats.RGB24, V4L2PixelFormats.BGR24);
        Assert.NotEqual(V4L2PixelFormats.RGB32, V4L2PixelFormats.BGR32);
    }
}