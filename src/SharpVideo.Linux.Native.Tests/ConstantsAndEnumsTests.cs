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
        Assert.Equal(6u, (uint)DrmModeEncoderType.DSI);
        Assert.Equal(7u, (uint)DrmModeEncoderType.DPMST);
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
        Assert.Equal(5u, (uint)V4L2Field.SEQ_TB);
        Assert.Equal(6u, (uint)V4L2Field.SEQ_BT);
        Assert.Equal(7u, (uint)V4L2Field.ALTERNATE);
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

    #region IoctlConstants Tests

    [Fact]
    public void TestIoctlConstants_DirectionBits()
    {
        // Test that ioctl direction bits have expected values
        Assert.Equal(0U, IoctlConstants.IOC_NONE);
        Assert.Equal(1U, IoctlConstants.IOC_WRITE);
        Assert.Equal(2U, IoctlConstants.IOC_READ);
    }

    [Fact]
    public void TestIoctlConstants_BitSizes()
    {
        // Test that ioctl bit sizes have expected values
        Assert.Equal(8, IoctlConstants.IOC_NRBITS);
        Assert.Equal(8, IoctlConstants.IOC_TYPEBITS);
        Assert.Equal(14, IoctlConstants.IOC_SIZEBITS);
        Assert.Equal(2, IoctlConstants.IOC_DIRBITS);
    }

    [Fact]
    public void TestIoctlConstants_Masks()
    {
        // Test that ioctl masks have expected values
        Assert.Equal(255, IoctlConstants.IOC_NRMASK); // (1 << 8) - 1
        Assert.Equal(255, IoctlConstants.IOC_TYPEMASK); // (1 << 8) - 1
        Assert.Equal(16383, IoctlConstants.IOC_SIZEMASK); // (1 << 14) - 1
        Assert.Equal(3, IoctlConstants.IOC_DIRMASK); // (1 << 2) - 1
    }

    [Fact]
    public void TestIoctlConstants_Shifts()
    {
        // Test that ioctl shift positions have expected values
        Assert.Equal(0, IoctlConstants.IOC_NRSHIFT);
        Assert.Equal(8, IoctlConstants.IOC_TYPESHIFT);
        Assert.Equal(16, IoctlConstants.IOC_SIZESHIFT);
        Assert.Equal(30, IoctlConstants.IOC_DIRSHIFT);
    }

    [Fact]
    public void TestIoctlConstants_MagicNumbers()
    {
        // Test that magic numbers have expected values
        Assert.Equal((uint)'H', IoctlConstants.DMA_HEAP_IOC_MAGIC);
        Assert.Equal((uint)'d', IoctlConstants.DRM_IOCTL_BASE);
        Assert.NotEqual(0u, IoctlConstants.DMA_HEAP_IOCTL_ALLOC);
    }

    #endregion

    #region DrmModeFlag Tests

    [Fact]
    public void TestDrmModeFlag_BasicFlags()
    {
        // Test that DRM mode flags have expected bit positions
        Assert.Equal(1u << 0, (uint)DrmModeFlag.DRM_MODE_FLAG_PHSYNC);
        Assert.Equal(1u << 1, (uint)DrmModeFlag.DRM_MODE_FLAG_NHSYNC);
        Assert.Equal(1u << 2, (uint)DrmModeFlag.DRM_MODE_FLAG_PVSYNC);
        Assert.Equal(1u << 3, (uint)DrmModeFlag.DRM_MODE_FLAG_NVSYNC);
        Assert.Equal(1u << 4, (uint)DrmModeFlag.DRM_MODE_FLAG_INTERLACE);
        Assert.Equal(1u << 5, (uint)DrmModeFlag.DRM_MODE_FLAG_DBLSCAN);
        Assert.Equal(1u << 6, (uint)DrmModeFlag.DRM_MODE_FLAG_CSYNC);
        Assert.Equal(1u << 7, (uint)DrmModeFlag.DRM_MODE_FLAG_PCSYNC);
        Assert.Equal(1u << 8, (uint)DrmModeFlag.DRM_MODE_FLAG_NCSYNC);
        Assert.Equal(1u << 9, (uint)DrmModeFlag.DRM_MODE_FLAG_HSKEW);
        Assert.Equal(1u << 10, (uint)DrmModeFlag.DRM_MODE_FLAG_BCAST);
        Assert.Equal(1u << 11, (uint)DrmModeFlag.DRM_MODE_FLAG_PIXMUX);
        Assert.Equal(1u << 12, (uint)DrmModeFlag.DRM_MODE_FLAG_DBLCLK);
        Assert.Equal(1u << 13, (uint)DrmModeFlag.DRM_MODE_FLAG_CLKDIV2);
    }

    [Fact]
    public void TestDrmModeFlag_3DFlags()
    {
        // Test that DRM 3D mode flags have expected values
        Assert.Equal(0x1fu << 14, (uint)DrmModeFlag.DRM_MODE_FLAG_3D_MASK);
        Assert.Equal(0u << 14, (uint)DrmModeFlag.DRM_MODE_FLAG_3D_NONE);
        Assert.Equal(1u << 14, (uint)DrmModeFlag.DRM_MODE_FLAG_3D_FRAME_PACKING);
        Assert.Equal(2u << 14, (uint)DrmModeFlag.DRM_MODE_FLAG_3D_FIELD_ALTERNATIVE);
        Assert.Equal(3u << 14, (uint)DrmModeFlag.DRM_MODE_FLAG_3D_LINE_ALTERNATIVE);
        Assert.Equal(4u << 14, (uint)DrmModeFlag.DRM_MODE_FLAG_3D_SIDE_BY_SIDE_FULL);
        Assert.Equal(5u << 14, (uint)DrmModeFlag.DRM_MODE_FLAG_3D_L_DEPTH);
        Assert.Equal(6u << 14, (uint)DrmModeFlag.DRM_MODE_FLAG_3D_L_DEPTH_GFX_GFX_DEPTH);
        Assert.Equal(7u << 14, (uint)DrmModeFlag.DRM_MODE_FLAG_3D_TOP_AND_BOTTOM);
        Assert.Equal(8u << 14, (uint)DrmModeFlag.DRM_MODE_FLAG_3D_SIDE_BY_SIDE_HALF);
    }

    #endregion

    #region PropertyType Tests

    [Fact]
    public void TestPropertyType_BasicFlags()
    {
        // Test that property type flags have expected bit positions
        Assert.Equal(1u << 1, (uint)PropertyType.DRM_MODE_PROP_RANGE);
        Assert.Equal(1u << 2, (uint)PropertyType.DRM_MODE_PROP_IMMUTABLE);
        Assert.Equal(1u << 3, (uint)PropertyType.DRM_MODE_PROP_ENUM);
        Assert.Equal(1u << 4, (uint)PropertyType.DRM_MODE_PROP_BLOB);
        Assert.Equal(1u << 5, (uint)PropertyType.DRM_MODE_PROP_BITMASK);
    }

    [Fact]
    public void TestPropertyType_CombinedFlags()
    {
        // Test that combined property type flags have expected values
        var expectedLegacy = (uint)(PropertyType.DRM_MODE_PROP_RANGE |
                                   PropertyType.DRM_MODE_PROP_ENUM |
                                   PropertyType.DRM_MODE_PROP_BLOB |
                                   PropertyType.DRM_MODE_PROP_BITMASK);
        Assert.Equal(expectedLegacy, (uint)PropertyType.DRM_MODE_PROP_LEGACY_TYPE);
        Assert.Equal(0x0000ffc0u, (uint)PropertyType.DRM_MODE_PROP_EXTENDED_TYPE);
    }

    #endregion

    #region Additional V4L2Constants Tests

    [Fact]
    public void TestV4L2Constants_AllBufferTypes()
    {
        // Test all V4L2 buffer type constants have expected values
        Assert.Equal(1u, V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE);
        Assert.Equal(2u, V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT);
        Assert.Equal(3u, V4L2Constants.V4L2_BUF_TYPE_VIDEO_OVERLAY);
        Assert.Equal(4u, V4L2Constants.V4L2_BUF_TYPE_VBI_CAPTURE);
        Assert.Equal(5u, V4L2Constants.V4L2_BUF_TYPE_VBI_OUTPUT);
        Assert.Equal(6u, V4L2Constants.V4L2_BUF_TYPE_SLICED_VBI_CAPTURE);
        Assert.Equal(7u, V4L2Constants.V4L2_BUF_TYPE_SLICED_VBI_OUTPUT);
        Assert.Equal(8u, V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT_OVERLAY);
        Assert.Equal(9u, V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE);
        Assert.Equal(10u, V4L2Constants.V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE);
        Assert.Equal(11u, V4L2Constants.V4L2_BUF_TYPE_SDR_CAPTURE);
        Assert.Equal(12u, V4L2Constants.V4L2_BUF_TYPE_SDR_OUTPUT);
        Assert.Equal(13u, V4L2Constants.V4L2_BUF_TYPE_META_CAPTURE);
        Assert.Equal(14u, V4L2Constants.V4L2_BUF_TYPE_META_OUTPUT);
    }

    [Fact]
    public void TestV4L2Constants_ControlConstants()
    {
        // Test V4L2 control class constants
        Assert.Equal(0x00980000u, V4L2Constants.V4L2_CTRL_CLASS_USER);
        Assert.Equal(0x00990000u, V4L2Constants.V4L2_CTRL_CLASS_CODEC);

        // Test computed control base
        Assert.Equal(V4L2Constants.V4L2_CTRL_CLASS_CODEC | 0x900u, V4L2Constants.V4L2_CID_CODEC_BASE);

        // Test H.264 control IDs
        Assert.Equal(V4L2Constants.V4L2_CID_CODEC_BASE + 302u, V4L2Constants.V4L2_CID_STATELESS_H264_SPS);
        Assert.Equal(V4L2Constants.V4L2_CID_CODEC_BASE + 303u, V4L2Constants.V4L2_CID_STATELESS_H264_PPS);
        Assert.Equal(V4L2Constants.V4L2_CID_CODEC_BASE + 304u, V4L2Constants.V4L2_CID_STATELESS_H264_SLICE_PARAMS);
        Assert.Equal(V4L2Constants.V4L2_CID_CODEC_BASE + 305u, V4L2Constants.V4L2_CID_STATELESS_H264_DECODE_PARAMS);
        Assert.Equal(V4L2Constants.V4L2_CID_CODEC_BASE + 306u, V4L2Constants.V4L2_CID_STATELESS_H264_START_CODE);
        Assert.Equal(V4L2Constants.V4L2_CID_CODEC_BASE + 307u, V4L2Constants.V4L2_CID_STATELESS_H264_DPB);
        Assert.Equal(V4L2Constants.V4L2_CID_CODEC_BASE + 308u, V4L2Constants.V4L2_CID_STATELESS_H264_DECODE_MODE);
    }

    [Fact]
    public void TestV4L2Constants_H264Values()
    {
        // Test H.264 start code values
        Assert.Equal(0u, V4L2Constants.V4L2_STATELESS_H264_START_CODE_NONE);
        Assert.Equal(1u, V4L2Constants.V4L2_STATELESS_H264_START_CODE_ANNEX_B);

        // Test H.264 decode mode values
        Assert.Equal(0u, V4L2Constants.V4L2_STATELESS_H264_DECODE_MODE_SLICE_BASED);
        Assert.Equal(1u, V4L2Constants.V4L2_STATELESS_H264_DECODE_MODE_FRAME_BASED);
    }

    [Fact]
    public void TestV4L2Constants_MiscConstants()
    {
        // Test miscellaneous V4L2 constants
        Assert.Equal(32u, V4L2Constants.VIDEO_MAX_FRAME);
        Assert.Equal(8u, V4L2Constants.VIDEO_MAX_PLANES);
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
    public void TestV4L2PixelFormats_AdditionalRGBFormats()
    {
        // Test additional RGB pixel format constants
        Assert.NotEqual(0u, V4L2PixelFormats.NV16M);
        Assert.NotEqual(0u, V4L2PixelFormats.NV61M);
        Assert.NotEqual(0u, V4L2PixelFormats.RGB444);
        Assert.NotEqual(0u, V4L2PixelFormats.ARGB444);
        Assert.NotEqual(0u, V4L2PixelFormats.XRGB444);
        Assert.NotEqual(0u, V4L2PixelFormats.RGB555);
        Assert.NotEqual(0u, V4L2PixelFormats.ARGB555);
        Assert.NotEqual(0u, V4L2PixelFormats.XRGB555);
        Assert.NotEqual(0u, V4L2PixelFormats.XBGR32);
        Assert.NotEqual(0u, V4L2PixelFormats.RGB32);
        Assert.NotEqual(0u, V4L2PixelFormats.ARGB32);
        Assert.NotEqual(0u, V4L2PixelFormats.XRGB32);

        // Verify they are all different
        var formats = new uint[] {
            V4L2PixelFormats.RGB444, V4L2PixelFormats.ARGB444, V4L2PixelFormats.XRGB444,
            V4L2PixelFormats.RGB555, V4L2PixelFormats.ARGB555, V4L2PixelFormats.XRGB555
        };
        for (int i = 0; i < formats.Length; i++)
        {
            for (int j = i + 1; j < formats.Length; j++)
            {
                Assert.NotEqual(formats[i], formats[j]);
            }
        }
    }

    [Fact]
    public void TestV4L2PixelFormats_YUVFormats()
    {
        // Test YUV pixel format constants
        Assert.NotEqual(0u, V4L2PixelFormats.GREY);
        Assert.NotEqual(0u, V4L2PixelFormats.Y10);
        Assert.NotEqual(0u, V4L2PixelFormats.Y12);
        Assert.NotEqual(0u, V4L2PixelFormats.Y16);
        Assert.NotEqual(0u, V4L2PixelFormats.YUYV);
        Assert.NotEqual(0u, V4L2PixelFormats.YVYU);
        Assert.NotEqual(0u, V4L2PixelFormats.UYVY);
        Assert.NotEqual(0u, V4L2PixelFormats.VYUY);
        Assert.NotEqual(0u, V4L2PixelFormats.YUV420);
        Assert.NotEqual(0u, V4L2PixelFormats.YVU420);
        Assert.NotEqual(0u, V4L2PixelFormats.YUV422P);
        Assert.NotEqual(0u, V4L2PixelFormats.YUV444P);

        // Test multiplanar YUV formats
        Assert.NotEqual(0u, V4L2PixelFormats.YUV420M);
        Assert.NotEqual(0u, V4L2PixelFormats.YVU420M);
        Assert.NotEqual(0u, V4L2PixelFormats.YUV422M);
        Assert.NotEqual(0u, V4L2PixelFormats.YVU422M);
        Assert.NotEqual(0u, V4L2PixelFormats.YUV444M);
        Assert.NotEqual(0u, V4L2PixelFormats.YVU444M);
    }

    [Fact]
    public void TestV4L2PixelFormats_CompressedFormats()
    {
        // Test compressed video format constants
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_MJPEG);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_JPEG);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_DV);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_MPEG);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_H264);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_H264_NO_SC);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_H264_MVC);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_H263);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_MPEG1);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_MPEG2);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_MPEG2_SLICE);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_MPEG4);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_XVID);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_VC1_ANNEX_G);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_VC1_ANNEX_L);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_VP8);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_VP8_FRAME);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_VP9);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_VP9_FRAME);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_HEVC);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_FWHT);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_FWHT_STATELESS);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_H264_SLICE);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_HEVC_SLICE);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_AV1_FRAME);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_SPK);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_RV30);
        Assert.NotEqual(0u, V4L2PixelFormats.V4L2_PIX_FMT_RV40);
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

    [Fact]
    public void TestV4L2Constants_AdditionalIoctls()
    {
        // Test additional V4L2 ioctl constants
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_RESERVED);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_ENUM_FMT);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_QUERYBUF);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_FBUF);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_S_FBUF);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_OVERLAY);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_EXPBUF);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_PARM);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_S_PARM);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_STD);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_S_STD);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_ENUMSTD);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_ENUMINPUT);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_CTRL);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_S_CTRL);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_TUNER);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_S_TUNER);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_AUDIO);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_S_AUDIO);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_QUERYCTRL);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_QUERYMENU);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_INPUT);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_S_INPUT);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_EDID);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_S_EDID);
    }

    [Fact]
    public void TestV4L2Constants_ExtendedIoctls()
    {
        // Test extended V4L2 ioctl constants
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_OUTPUT);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_S_OUTPUT);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_ENUMOUTPUT);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_AUDOUT);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_S_AUDOUT);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_MODULATOR);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_S_MODULATOR);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_FREQUENCY);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_S_FREQUENCY);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_CROPCAP);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_CROP);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_S_CROP);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_JPEGCOMP);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_S_JPEGCOMP);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_QUERYSTD);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_TRY_FMT);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_ENUMAUDIO);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_ENUMAUDOUT);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_PRIORITY);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_S_PRIORITY);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_SLICED_VBI_CAP);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_LOG_STATUS);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_EXT_CTRLS);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_S_EXT_CTRLS);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_TRY_EXT_CTRLS);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_ENUM_FRAMESIZES);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_ENUM_FRAMEINTERVALS);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_G_ENC_INDEX);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_ENCODER_CMD);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_TRY_ENCODER_CMD);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_DECODER_CMD);
        Assert.NotEqual(0u, V4L2Constants.VIDIOC_TRY_DECODER_CMD);
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
        Assert.Equal(typeof(uint), Enum.GetUnderlyingType(typeof(DrmModeFlag)));
        Assert.Equal(typeof(uint), Enum.GetUnderlyingType(typeof(PropertyType)));
    }

    [Fact]
    public void TestLibcConstants()
    {
        // Test that Libc constants have expected values
        Assert.Equal(new IntPtr(-1), Libc.MAP_FAILED);
    }

    #endregion
}