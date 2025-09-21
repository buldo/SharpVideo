using System;
using Xunit;

namespace SharpVideo.Linux.Native.Tests;

/// <summary>
/// Tests for constants and enum values to ensure they have expected values and behaviors
/// </summary>
public class ConstantsAndEnumsTests
{
    #region Enum Value Tests

    [Fact]
    public void TestConnectorType_HasExpectedValues()
    {
        // Test that connector types have expected numeric values
        Assert.Equal(0u, (uint)ConnectorType.Unknown);
        Assert.Equal(1u, (uint)ConnectorType.VGA);
        Assert.Equal(2u, (uint)ConnectorType.DVII);
        Assert.Equal(3u, (uint)ConnectorType.DVID);
        Assert.Equal(4u, (uint)ConnectorType.DVIA);
        Assert.Equal(11u, (uint)ConnectorType.HDMIA);
        Assert.Equal(12u, (uint)ConnectorType.HDMIB);
        Assert.Equal(20u, (uint)ConnectorType.USB);
    }

    [Fact]
    public void TestDrmModeEncoderType_HasExpectedValues()
    {
        // Test that encoder types have expected numeric values
        Assert.Equal(0u, (uint)DrmModeEncoderType.NONE);
        Assert.Equal(1u, (uint)DrmModeEncoderType.DAC);
        Assert.Equal(2u, (uint)DrmModeEncoderType.TMDS);
        Assert.Equal(3u, (uint)DrmModeEncoderType.LVDS);
        Assert.Equal(4u, (uint)DrmModeEncoderType.TVDAC);
        Assert.Equal(5u, (uint)DrmModeEncoderType.VIRTUAL);
        Assert.Equal(8u, (uint)DrmModeEncoderType.DPI);
    }

    [Fact]
    public void TestDrmModeConnection_HasExpectedValues()
    {
        // Test that connection states have expected numeric values
        Assert.Equal(1u, (uint)DrmModeConnection.Connected);
        Assert.Equal(2u, (uint)DrmModeConnection.Disconnected);
        Assert.Equal(3u, (uint)DrmModeConnection.Unknown);
    }

    [Fact]
    public void TestDrmModeSubPixel_HasExpectedValues()
    {
        // Test that sub-pixel orders have expected numeric values
        Assert.Equal(1u, (uint)DrmModeSubPixel.Unknown);
        Assert.Equal(2u, (uint)DrmModeSubPixel.HorizontalRgb);
        Assert.Equal(3u, (uint)DrmModeSubPixel.HorizontalBgr);
        Assert.Equal(4u, (uint)DrmModeSubPixel.VerticalRgb);
        Assert.Equal(5u, (uint)DrmModeSubPixel.VerticalBgr);
        Assert.Equal(6u, (uint)DrmModeSubPixel.None);
    }

    [Fact]
    public void TestV4L2BufferType_HasExpectedValues()
    {
        // Test that V4L2 buffer types have expected numeric values
        Assert.Equal(1u, (uint)V4L2BufferType.VIDEO_CAPTURE);
        Assert.Equal(2u, (uint)V4L2BufferType.VIDEO_OUTPUT);
        Assert.Equal(9u, (uint)V4L2BufferType.VIDEO_CAPTURE_MPLANE);
        Assert.Equal(10u, (uint)V4L2BufferType.VIDEO_OUTPUT_MPLANE);
        Assert.Equal(13u, (uint)V4L2BufferType.META_CAPTURE);
        Assert.Equal(14u, (uint)V4L2BufferType.META_OUTPUT);
    }

    [Fact]
    public void TestV4L2Memory_HasExpectedValues()
    {
        // Test that V4L2 memory types have expected numeric values
        Assert.Equal(1u, (uint)V4L2Memory.MMAP);
        Assert.Equal(2u, (uint)V4L2Memory.USERPTR);
        Assert.Equal(3u, (uint)V4L2Memory.OVERLAY);
        Assert.Equal(4u, (uint)V4L2Memory.DMABUF);
    }

    [Fact]
    public void TestV4L2Field_HasExpectedValues()
    {
        // Test that V4L2 field types have expected numeric values
        Assert.Equal(0u, (uint)V4L2Field.ANY);
        Assert.Equal(1u, (uint)V4L2Field.NONE);
        Assert.Equal(2u, (uint)V4L2Field.TOP);
        Assert.Equal(3u, (uint)V4L2Field.BOTTOM);
        Assert.Equal(4u, (uint)V4L2Field.INTERLACED);
        Assert.Equal(8u, (uint)V4L2Field.INTERLACED_TB);
        Assert.Equal(9u, (uint)V4L2Field.INTERLACED_BT);
    }

    [Fact]
    public void TestV4L2DecoderCommand_HasExpectedValues()
    {
        // Test that V4L2 decoder commands have expected numeric values
        Assert.Equal(0u, (uint)V4L2DecoderCommand.START);
        Assert.Equal(1u, (uint)V4L2DecoderCommand.STOP);
        Assert.Equal(2u, (uint)V4L2DecoderCommand.PAUSE);
        Assert.Equal(3u, (uint)V4L2DecoderCommand.RESUME);
        Assert.Equal(4u, (uint)V4L2DecoderCommand.FLUSH);
    }

    [Fact]
    public void TestV4L2EncoderCommand_HasExpectedValues()
    {
        // Test that V4L2 encoder commands have expected numeric values
        Assert.Equal(0u, (uint)V4L2EncoderCommand.START);
        Assert.Equal(1u, (uint)V4L2EncoderCommand.STOP);
        Assert.Equal(2u, (uint)V4L2EncoderCommand.PAUSE);
        Assert.Equal(3u, (uint)V4L2EncoderCommand.RESUME);
    }

    [Fact]
    public void TestOpenFlags_HasExpectedValues()
    {
        // Test that open flags have expected numeric values (POSIX standard)
        Assert.Equal(0x000, (int)OpenFlags.O_RDONLY);
        Assert.Equal(0x001, (int)OpenFlags.O_WRONLY);
        Assert.Equal(0x002, (int)OpenFlags.O_RDWR);
        Assert.Equal(0x040, (int)OpenFlags.O_CREAT);
        Assert.Equal(0x080, (int)OpenFlags.O_EXCL);
        Assert.Equal(0x200, (int)OpenFlags.O_TRUNC);
        Assert.Equal(0x400, (int)OpenFlags.O_APPEND);
        Assert.Equal(0x800, (int)OpenFlags.O_NONBLOCK);
    }

    [Fact]
    public void TestProtFlags_HasExpectedValues()
    {
        // Test that protection flags have expected numeric values (POSIX standard)
        Assert.Equal(0x0, (int)ProtFlags.PROT_NONE);
        Assert.Equal(0x1, (int)ProtFlags.PROT_READ);
        Assert.Equal(0x2, (int)ProtFlags.PROT_WRITE);
        Assert.Equal(0x4, (int)ProtFlags.PROT_EXEC);
    }

    [Fact]
    public void TestMapFlags_HasExpectedValues()
    {
        // Test that mapping flags have expected numeric values (POSIX standard)
        Assert.Equal(0x01, (int)MapFlags.MAP_SHARED);
        Assert.Equal(0x02, (int)MapFlags.MAP_PRIVATE);
        Assert.Equal(0x10, (int)MapFlags.MAP_FIXED);
    }

    [Fact]
    public void TestMsyncFlags_HasExpectedValues()
    {
        // Test that msync flags have expected numeric values (POSIX standard)
        Assert.Equal(1, (int)MsyncFlags.MS_ASYNC);
        Assert.Equal(0, (int)MsyncFlags.MS_SYNC);
        Assert.Equal(2, (int)MsyncFlags.MS_INVALIDATE);
    }

    #endregion

    #region Flag Combinations Tests

    [Fact]
    public void TestV4L2BufferFlags_FlagCombinations()
    {
        // Test that flags can be combined properly
        var mapped = V4L2BufferFlags.MAPPED;
        var queued = V4L2BufferFlags.QUEUED;
        var combined = mapped | queued;

        Assert.True((combined & mapped) != 0);
        Assert.True((combined & queued) != 0);
        Assert.NotEqual(mapped, queued);
    }

    [Fact]
    public void TestV4L2Capabilities_FlagCombinations()
    {
        // Test that capabilities can be combined properly
        var videoCapture = V4L2Capabilities.VIDEO_CAPTURE;
        var streaming = V4L2Capabilities.STREAMING;
        var combined = videoCapture | streaming;

        Assert.True((combined & videoCapture) != 0);
        Assert.True((combined & streaming) != 0);
        Assert.NotEqual(videoCapture, streaming);
    }

    [Fact]
    public void TestDrmModeFlag_FlagCombinations()
    {
        // Test that DRM mode flags can be combined properly
        var nhsync = DrmModeFlag.DRM_MODE_FLAG_NHSYNC;
        var nvsync = DrmModeFlag.DRM_MODE_FLAG_NVSYNC;
        var combined = nhsync | nvsync;

        Assert.True((combined & nhsync) != 0);
        Assert.True((combined & nvsync) != 0);
        Assert.NotEqual(nhsync, nvsync);
    }

    [Fact]
    public void TestPropertyType_FlagCombinations()
    {
        // Test that property type flags can be combined properly
        var range = PropertyType.DRM_MODE_PROP_RANGE;
        var immutable = PropertyType.DRM_MODE_PROP_IMMUTABLE;
        var combined = range | immutable;

        Assert.True((combined & range) != 0);
        Assert.True((combined & immutable) != 0);
        Assert.NotEqual(range, immutable);
    }

    #endregion

    #region Constant Value Tests

    [Fact]
    public void TestV4L2PixelFormats_CommonFormats()
    {
        // Test that common pixel format constants are defined and have valid FOURCC values
        Assert.NotEqual(0u, V4L2PixelFormats.NV12M);
        Assert.NotEqual(0u, V4L2PixelFormats.NV21M);
        Assert.NotEqual(0u, V4L2PixelFormats.RGB332);
        Assert.NotEqual(0u, V4L2PixelFormats.RGB565);
        Assert.NotEqual(0u, V4L2PixelFormats.BGR24);
        Assert.NotEqual(0u, V4L2PixelFormats.RGB24);
        Assert.NotEqual(0u, V4L2PixelFormats.BGR32);
        Assert.NotEqual(0u, V4L2PixelFormats.ABGR32);

        // Test that different formats have different values
        Assert.NotEqual(V4L2PixelFormats.NV12M, V4L2PixelFormats.NV21M);
        Assert.NotEqual(V4L2PixelFormats.RGB24, V4L2PixelFormats.BGR24);
    }

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

    #endregion

    #region Struct Size Tests

    [Fact]
    public void TestIoctlResult_StructSize()
    {
        // Test that IoctlResult has reasonable size
        int size = System.Runtime.InteropServices.Marshal.SizeOf<IoctlResult>();
        Assert.True(size > 0);
        Assert.True(size < 1000); // Sanity check - should be small structure
    }

    [Fact]
    public void TestIoctlResultWithDetails_StructSize()
    {
        // Test that IoctlResultWithDetails has reasonable size
        int size = System.Runtime.InteropServices.Marshal.SizeOf<IoctlResultWithDetails>();
        Assert.True(size > 0);
        Assert.True(size < 1000); // Sanity check - should be small structure
    }

    [Fact]
    public void TestTimeVal_StructSize()
    {
        // Test that TimeVal has expected size (should be 2 longs = 16 bytes on 64-bit)
        int size = System.Runtime.InteropServices.Marshal.SizeOf<TimeVal>();
        Assert.Equal(16, size); // 2 * sizeof(long) on 64-bit platforms
    }

    [Fact]
    public void TestV4L2Fract_StructSize()
    {
        // Test that V4L2Fract has expected size (should be 2 uints = 8 bytes)
        int size = System.Runtime.InteropServices.Marshal.SizeOf<V4L2Fract>();
        Assert.Equal(8, size); // 2 * sizeof(uint)
    }

    #endregion

    #region Enum Type Consistency Tests

    [Fact]
    public void TestAllEnums_ArePublic()
    {
        // Verify that key enums are public and accessible
        var connectorType = typeof(ConnectorType);
        var encoderType = typeof(DrmModeEncoderType);
        var bufferType = typeof(V4L2BufferType);
        var memoryType = typeof(V4L2Memory);
        var capabilities = typeof(V4L2Capabilities);

        Assert.True(connectorType.IsPublic);
        Assert.True(encoderType.IsPublic);
        Assert.True(bufferType.IsPublic);
        Assert.True(memoryType.IsPublic);
        Assert.True(capabilities.IsPublic);

        Assert.True(connectorType.IsEnum);
        Assert.True(encoderType.IsEnum);
        Assert.True(bufferType.IsEnum);
        Assert.True(memoryType.IsEnum);
        Assert.True(capabilities.IsEnum);
    }

    [Fact]
    public void TestEnums_HaveCorrectUnderlyingTypes()
    {
        // Verify that enums have correct underlying types
        Assert.Equal(typeof(uint), Enum.GetUnderlyingType(typeof(ConnectorType)));
        Assert.Equal(typeof(uint), Enum.GetUnderlyingType(typeof(DrmModeEncoderType)));
        Assert.Equal(typeof(uint), Enum.GetUnderlyingType(typeof(V4L2BufferType)));
        Assert.Equal(typeof(uint), Enum.GetUnderlyingType(typeof(V4L2Memory)));
        Assert.Equal(typeof(uint), Enum.GetUnderlyingType(typeof(V4L2Capabilities)));
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(OpenFlags)));
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(ProtFlags)));
    }

    #endregion
}