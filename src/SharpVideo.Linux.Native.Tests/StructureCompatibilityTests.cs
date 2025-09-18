using System.Runtime.InteropServices;
using Xunit;
using System.Linq;

namespace SharpVideo.Linux.Native.Tests;

/// <summary>
/// Tests to verify bit-to-bit compatibility between C# P/Invoke structures and their C counterparts
/// </summary>
public unsafe class StructureCompatibilityTests
{

    [Fact]
    public void TestDrmModeRes_NativeSizeCompatibility()
    {
        // Test that our DrmModeRes structure has the same size as the native drmModeRes structure
        int csharpSize = Marshal.SizeOf<DrmModeRes>();
        int nativeSize = NativeTestLibrary.GetNativeDrmModeResSize();

        Assert.Equal(nativeSize, csharpSize);
    }

    [Fact]
    public void TestDrmModeRes_NativeMemoryLayoutCompatibility()
    {
        // Fill C structure in native code and check that managed structure fields have right values
        var nativeFilledStruct = new DrmModeRes();

        // Fill structure using native C code
        NativeTestLibrary.FillNativeDrmModeRes(&nativeFilledStruct);

        // Verify that the managed structure fields have the expected values
        Assert.Equal(2, nativeFilledStruct.CountFbs);
        Assert.Equal(3, nativeFilledStruct.CountCrtcs);
        Assert.Equal(4, nativeFilledStruct.CountConnectors);
        Assert.Equal(5, nativeFilledStruct.CountEncoders);
        Assert.Equal(640, nativeFilledStruct.MinWidth);
        Assert.Equal(1920, nativeFilledStruct.MaxWidth);
        Assert.Equal(480, nativeFilledStruct.MinHeight);
        Assert.Equal(1080, nativeFilledStruct.MaxHeight);
    }

    [Fact]
    public void TestDrmModeEncoder_NativeSizeCompatibility()
    {
        // Test that our DrmModeEncoder structure has the same size as the native drmModeEncoder structure
        int csharpSize = Marshal.SizeOf<DrmModeEncoder>();
        int nativeSize = NativeTestLibrary.GetNativeDrmModeEncoderSize();

        Assert.Equal(nativeSize, csharpSize);
    }

    [Fact]
    public void TestDrmModeEncoder_NativeMemoryLayoutCompatibility()
    {
        // Fill C structure in native code and check that managed structure fields have right values
        var nativeFilledStruct = new DrmModeEncoder();

        // Fill structure using native C code
        NativeTestLibrary.FillNativeDrmModeEncoder(&nativeFilledStruct);

        // Verify that the managed structure fields have the expected values
        Assert.Equal(100u, nativeFilledStruct.EncoderId);
        Assert.Equal(DrmModeEncoderType.DAC, nativeFilledStruct.EncoderType);
        Assert.Equal(200u, nativeFilledStruct.CrtcId);
        Assert.Equal(0x07u, nativeFilledStruct.PossibleCrtcs);
        Assert.Equal(0x03u, nativeFilledStruct.PossibleClones);
    }

    [Fact]
    public void TestDrmModeConnector_NativeSizeCompatibility()
    {
        // Test that our DrmModeConnector structure has the same size as the native drmModeConnector structure
        int csharpSize = Marshal.SizeOf<DrmModeConnector>();
        int nativeSize = NativeTestLibrary.GetNativeDrmModeConnectorSize();

        Assert.Equal(nativeSize, csharpSize);
    }

    [Fact]
    public void TestDrmModeConnector_NativeMemoryLayoutCompatibility()
    {
        // Fill C structure in native code and check that managed structure fields have right values
        var nativeFilledStruct = new DrmModeConnector();

        // Fill structure using native C code
        NativeTestLibrary.FillNativeDrmModeConnector(&nativeFilledStruct);

        // Verify that the managed structure fields have the expected values
        Assert.Equal(300u, nativeFilledStruct.ConnectorId);
        Assert.Equal(100u, nativeFilledStruct.EncoderId);
        Assert.Equal(ConnectorType.HDMIA, nativeFilledStruct.ConnectorType); // DRM_MODE_CONNECTOR_HDMIA
        Assert.Equal(1u, nativeFilledStruct.ConnectorTypeId);
        Assert.Equal(DrmModeConnection.Connected, nativeFilledStruct.Connection);
        Assert.Equal(510u, nativeFilledStruct.MmWidth);
        Assert.Equal(287u, nativeFilledStruct.MmHeight);
        Assert.Equal(DrmModeSubPixel.HorizontalRgb, nativeFilledStruct.SubPixel);
        Assert.Equal(0, nativeFilledStruct.CountModes);
        Assert.Equal(0, nativeFilledStruct.CountProps);
        Assert.Equal(0, nativeFilledStruct.CountEncoders);
    }

    [Fact]
    public void TestDrmModeCrtc_NativeSizeCompatibility()
    {
        // Test that our DrmModeCrtc structure has the same size as the native drmModeCrtc structure
        int csharpSize = Marshal.SizeOf<DrmModeCrtc>();
        int nativeSize = NativeTestLibrary.GetNativeDrmModeCrtcSize();

        Assert.Equal(nativeSize, csharpSize);
    }

    [Fact]
    public void TestDrmModeCrtc_NativeMemoryLayoutCompatibility()
    {
        // Fill C structure in native code and check that managed structure fields have right values
        var nativeFilledStruct = new DrmModeCrtc();

        // Fill structure using native C code
        NativeTestLibrary.FillNativeDrmModeCrtc(&nativeFilledStruct);

        // Verify that the managed structure fields have the expected values
        Assert.Equal(400u, nativeFilledStruct.CrtcId);
        Assert.Equal(500u, nativeFilledStruct.BufferId);
        Assert.Equal(0u, nativeFilledStruct.X);
        Assert.Equal(0u, nativeFilledStruct.Y);
        Assert.Equal(1920u, nativeFilledStruct.Width);
        Assert.Equal(1080u, nativeFilledStruct.Height);
        Assert.Equal(1, nativeFilledStruct.ModeValid);
        Assert.Equal(256, nativeFilledStruct.GammaSize);

        // Verify embedded mode info
        Assert.Equal(148500u, nativeFilledStruct.Mode.Clock);
        Assert.Equal(1920, nativeFilledStruct.Mode.HDisplay);
        Assert.Equal(1080, nativeFilledStruct.Mode.VDisplay);
        Assert.Equal(60u, nativeFilledStruct.Mode.VRefresh);
        Assert.Equal("1920x1080", nativeFilledStruct.Mode.NameString);
    }

    [Fact]
    public void TestDrmModeModeInfo_NativeSizeCompatibility()
    {
        // Test that our DrmModeModeInfo structure has the same size as the native drmModeModeInfo structure
        int csharpSize = Marshal.SizeOf<DrmModeModeInfo>();
        int nativeSize = NativeTestLibrary.GetNativeDrmModeModeInfoSize();

        Assert.Equal(nativeSize, csharpSize);
    }

    [Fact]
    public void TestDrmModeModeInfo_NativeMemoryLayoutCompatibility()
    {
        // Fill C structure in native code and check that managed structure fields have right values
        var nativeFilledStruct = new DrmModeModeInfo();

        // Fill structure using native C code
        NativeTestLibrary.FillNativeDrmModeModeInfo(&nativeFilledStruct);

        // Verify that the managed structure fields have the expected values
        Assert.Equal(148500u, nativeFilledStruct.Clock);
        Assert.Equal(1920, nativeFilledStruct.HDisplay);
        Assert.Equal(2008, nativeFilledStruct.HSyncStart);
        Assert.Equal(2052, nativeFilledStruct.HSyncEnd);
        Assert.Equal(2200, nativeFilledStruct.HTotal);
        Assert.Equal(0, nativeFilledStruct.HSkew);
        Assert.Equal(1080, nativeFilledStruct.VDisplay);
        Assert.Equal(1084, nativeFilledStruct.VSyncStart);
        Assert.Equal(1089, nativeFilledStruct.VSyncEnd);
        Assert.Equal(1125, nativeFilledStruct.VTotal);
        Assert.Equal(0, nativeFilledStruct.VScan);
        Assert.Equal(60u, nativeFilledStruct.VRefresh);
        Assert.Equal(DrmModeFlag.DRM_MODE_FLAG_NHSYNC | DrmModeFlag.DRM_MODE_FLAG_NVSYNC, nativeFilledStruct.Flags); // DRM_MODE_FLAG_NHSYNC | DRM_MODE_FLAG_NVSYNC
        Assert.Equal(DrmModeType.PREFERRED, nativeFilledStruct.Type); // DRM_MODE_TYPE_PREFERRED
        Assert.Equal("1920x1080", nativeFilledStruct.NameString);
    }

    [Fact]
    public void TestDrmModePlane_NativeSizeCompatibility()
    {
        // Test that our DrmModePlane structure has the same size as the native drmModePlane structure
        int csharpSize = Marshal.SizeOf<DrmModePlane>();
        int nativeSize = NativeTestLibrary.GetNativeDrmModePlaneSize();

        Assert.Equal(nativeSize, csharpSize);
    }

    [Fact]
    public void TestDrmModePlane_NativeMemoryLayoutCompatibility()
    {
        // Fill C structure in native code and check that managed structure fields have right values
        var nativeFilledStruct = new DrmModePlane();

        // Fill structure using native C code
        NativeTestLibrary.FillNativeDrmModePlane(&nativeFilledStruct);

        // Verify that the managed structure fields have the expected values
        Assert.Equal(0u, nativeFilledStruct.CountFormats);
        Assert.Equal(600u, nativeFilledStruct.PlaneId);
        Assert.Equal(400u, nativeFilledStruct.CrtcId);
        Assert.Equal(500u, nativeFilledStruct.FbId);
        Assert.Equal(0u, nativeFilledStruct.CrtcX);
        Assert.Equal(0u, nativeFilledStruct.CrtcY);
        Assert.Equal(0u, nativeFilledStruct.X);
        Assert.Equal(0u, nativeFilledStruct.Y);
        Assert.Equal(0x07u, nativeFilledStruct.PossibleCrtcs);
        Assert.Equal(256u, nativeFilledStruct.GammaSize);
    }

    [Fact]
    public void TestDrmModeFB_NativeSizeCompatibility()
    {
        // Test that our DrmModeFB structure has the same size as the native drmModeFB structure
        int csharpSize = Marshal.SizeOf<DrmModeFB>();
        int nativeSize = NativeTestLibrary.GetNativeDrmModeFBSize();

        Assert.Equal(nativeSize, csharpSize);
    }

    [Fact]
    public void TestDrmModeFB_NativeMemoryLayoutCompatibility()
    {
        // Fill C structure in native code and check that managed structure fields have right values
        var nativeFilledStruct = new DrmModeFB();

        // Fill structure using native C code
        NativeTestLibrary.FillNativeDrmModeFB(&nativeFilledStruct);

        // Verify that the managed structure fields have the expected values
        Assert.Equal(700u, nativeFilledStruct.FbId);
        Assert.Equal(1920u, nativeFilledStruct.Width);
        Assert.Equal(1080u, nativeFilledStruct.Height);
        Assert.Equal(7680u, nativeFilledStruct.Pitch); // 1920 * 4 bytes per pixel
        Assert.Equal(32u, nativeFilledStruct.Bpp);
        Assert.Equal(24u, nativeFilledStruct.Depth);
        Assert.Equal(800u, nativeFilledStruct.Handle);
    }

    [Fact]
    public void TestDmaHeapAllocationData_NativeSizeCompatibility()
    {
        // Test that our DmaHeapAllocationData structure has the same size as the native dma_heap_allocation_data structure
        int csharpSize = Marshal.SizeOf<DmaHeapAllocationData>();
        int nativeSize = NativeTestLibrary.GetNativeDmaHeapAllocationDataSize();

        Assert.Equal(nativeSize, csharpSize);
    }

    [Fact]
    public void TestDmaHeapAllocationData_NativeMemoryLayoutCompatibility()
    {
        // Fill C structure in native code and check that managed structure fields have right values
        var nativeFilledStruct = new DmaHeapAllocationData();

        // Fill structure using native C code
        NativeTestLibrary.FillNativeDmaHeapAllocationData(&nativeFilledStruct);

        // Verify that the managed structure fields have the expected values
        Assert.Equal(4096ul, nativeFilledStruct.len);
        Assert.Equal(42u, nativeFilledStruct.fd);
        Assert.Equal(0x80002u, nativeFilledStruct.fd_flags); // O_RDWR | O_CLOEXEC
        Assert.Equal(0ul, nativeFilledStruct.heap_flags); // No heap flags defined yet
    }

    [Fact]
    public void TestOffT_NativeSizeCompatibility()
    {
        // Test that our IntPtr has the same size as the native __off_t type
        int csharpSize = Marshal.SizeOf<nint>();
        int nativeSize = NativeTestLibrary.GetOffTSize();

        Assert.Equal(nativeSize, csharpSize);
    }

    // V4L2 Structure Compatibility Tests

    [Fact]
    public void TestV4L2Capability_NativeSizeCompatibility()
    {
        // Test that our V4L2Capability structure has the same size as the native v4l2_capability structure
        int csharpSize = Marshal.SizeOf<V4L2Capability>();
        int nativeSize = NativeTestLibrary.GetNativeV4L2CapabilitySize();

        Assert.Equal(nativeSize, csharpSize);
    }

    [Fact]
    public void TestV4L2Capability_NativeMemoryLayoutCompatibility()
    {
        // Fill C structure in native code and check that managed structure fields have right values
        var nativeFilledStruct = new V4L2Capability();

        // Fill structure using native C code
        NativeTestLibrary.FillNativeV4L2Capability(&nativeFilledStruct);

        // Verify that the managed structure fields have the expected values
        Assert.Equal("test_driver", nativeFilledStruct.DriverString);
        Assert.Equal("Test Video Device", nativeFilledStruct.CardString);
        Assert.Equal("platform:test-video", nativeFilledStruct.BusInfoString);
        Assert.Equal(0x050C00u, nativeFilledStruct.Version); // Kernel version 5.12.0
        Assert.True((nativeFilledStruct.Capabilities & (uint)V4L2Capabilities.VIDEO_CAPTURE) != 0);
        Assert.True((nativeFilledStruct.Capabilities & (uint)V4L2Capabilities.VIDEO_CAPTURE_MPLANE) != 0);
        Assert.True((nativeFilledStruct.Capabilities & (uint)V4L2Capabilities.STREAMING) != 0);
        Assert.True((nativeFilledStruct.DeviceCaps & (uint)V4L2Capabilities.VIDEO_CAPTURE_MPLANE) != 0);
        Assert.True((nativeFilledStruct.DeviceCaps & (uint)V4L2Capabilities.STREAMING) != 0);
    }

    [Fact]
    public void TestV4L2PixFormatMplane_NativeSizeCompatibility()
    {
        // Test that our V4L2PixFormatMplane structure has the same size as the native v4l2_pix_format_mplane structure
        int csharpSize = Marshal.SizeOf<V4L2PixFormatMplane>();
        int nativeSize = NativeTestLibrary.GetNativeV4L2PixFormatMplaneSize();

        Assert.Equal(nativeSize, csharpSize);
    }

    [Fact]
    public void TestV4L2PixFormatMplane_NativeMemoryLayoutCompatibility()
    {
        // Fill C structure in native code and check that managed structure fields have right values
        var nativeFilledStruct = new V4L2PixFormatMplane();

        // Fill structure using native C code
        NativeTestLibrary.FillNativeV4L2PixFormatMplane(&nativeFilledStruct);

        // Verify that the managed structure fields have the expected values
        Assert.Equal(1920u, nativeFilledStruct.Width);
        Assert.Equal(1080u, nativeFilledStruct.Height);
        Assert.Equal(V4L2PixelFormats.NV12M, nativeFilledStruct.PixelFormat);
        Assert.Equal((uint)V4L2Field.NONE, nativeFilledStruct.Field);
        Assert.Equal(2, nativeFilledStruct.NumPlanes);

        // Check plane format information
        var planeFormats = nativeFilledStruct.PlaneFormats;
        Assert.Equal(1920u * 1080u, planeFormats[0].SizeImage);
        Assert.Equal(1920u, planeFormats[0].BytesPerLine);
        Assert.Equal(1920u * 1080u / 2u, planeFormats[1].SizeImage);
        Assert.Equal(1920u, planeFormats[1].BytesPerLine);
    }

    [Fact]
    public void TestV4L2Format_NativeSizeCompatibility()
    {
        // Test that our V4L2Format structure has the same size as the native v4l2_format structure
        int csharpSize = Marshal.SizeOf<V4L2Format>();
        int nativeSize = NativeTestLibrary.GetNativeV4L2FormatSize();

        Assert.Equal(nativeSize, csharpSize);
    }

    [Fact]
    public void TestV4L2Format_NativeMemoryLayoutCompatibility()
    {
        // Fill C structure in native code and check that managed structure fields have right values
        var nativeFilledStruct = new V4L2Format();

        // Fill structure using native C code
        NativeTestLibrary.FillNativeV4L2Format(&nativeFilledStruct);

        // Verify that the managed structure fields have the expected values
        Assert.Equal(V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE, nativeFilledStruct.Type);
        Assert.Equal(1920u, nativeFilledStruct.Pix_mp.Width);
        Assert.Equal(1080u, nativeFilledStruct.Pix_mp.Height);
        Assert.Equal(V4L2PixelFormats.NV12M, nativeFilledStruct.Pix_mp.PixelFormat);
        Assert.Equal(2, nativeFilledStruct.Pix_mp.NumPlanes);
    }

    [Fact]
    public void TestV4L2RequestBuffers_NativeSizeCompatibility()
    {
        // Test that our V4L2RequestBuffers structure has the same size as the native v4l2_requestbuffers structure
        int csharpSize = Marshal.SizeOf<V4L2RequestBuffers>();
        int nativeSize = NativeTestLibrary.GetNativeV4L2RequestBuffersSize();

        Assert.Equal(nativeSize, csharpSize);
    }

    [Fact]
    public void TestV4L2RequestBuffers_NativeMemoryLayoutCompatibility()
    {
        // Fill C structure in native code and check that managed structure fields have right values
        var nativeFilledStruct = new V4L2RequestBuffers();

        // Fill structure using native C code
        NativeTestLibrary.FillNativeV4L2RequestBuffers(&nativeFilledStruct);

        // Verify that the managed structure fields have the expected values
        Assert.Equal(4u, nativeFilledStruct.Count);
        Assert.Equal(V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE, nativeFilledStruct.Type);
        Assert.Equal(V4L2Constants.V4L2_MEMORY_DMABUF, nativeFilledStruct.Memory);
    }

    [Fact]
    public void TestV4L2Buffer_NativeSizeCompatibility()
    {
        // Test that our V4L2Buffer structure has the same size as the native v4l2_buffer structure
        int csharpSize = Marshal.SizeOf<V4L2Buffer>();
        int nativeSize = NativeTestLibrary.GetNativeV4L2BufferSize();

        Assert.Equal(nativeSize, csharpSize);
    }

    [Fact]
    public void TestV4L2Buffer_NativeMemoryLayoutCompatibility()
    {
        // Fill C structure in native code and check that managed structure fields have right values
        var nativeFilledStruct = new V4L2Buffer();

        // Fill structure using native C code
        NativeTestLibrary.FillNativeV4L2Buffer(&nativeFilledStruct);

        // Verify that the managed structure fields have the expected values
        Assert.Equal(0u, nativeFilledStruct.Index);
        Assert.Equal(V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE, nativeFilledStruct.Type);
        Assert.Equal((uint)V4L2Field.NONE, nativeFilledStruct.Field);
        Assert.Equal(12345L, nativeFilledStruct.TimestampSec);
        Assert.Equal(67890L, nativeFilledStruct.TimestampUsec);
        Assert.Equal(123u, nativeFilledStruct.Sequence);
        Assert.Equal(V4L2Constants.V4L2_MEMORY_DMABUF, nativeFilledStruct.Memory);
        Assert.Equal(2u, nativeFilledStruct.Length); // Number of planes
    }

    [Fact]
    public void TestV4L2ExportBuffer_NativeSizeCompatibility()
    {
        // Test that our V4L2ExportBuffer structure has the same size as the native v4l2_exportbuffer structure
        int csharpSize = Marshal.SizeOf<V4L2ExportBuffer>();
        int nativeSize = NativeTestLibrary.GetNativeV4L2ExportBufferSize();

        Assert.Equal(nativeSize, csharpSize);
    }

    [Fact]
    public void TestV4L2ExportBuffer_NativeMemoryLayoutCompatibility()
    {
        // Fill C structure in native code and check that managed structure fields have right values
        var nativeFilledStruct = new V4L2ExportBuffer();

        // Fill structure using native C code
        NativeTestLibrary.FillNativeV4L2ExportBuffer(&nativeFilledStruct);

        // Verify that the managed structure fields have the expected values
        Assert.Equal(V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE, nativeFilledStruct.Type);
        Assert.Equal(0u, nativeFilledStruct.Index);
        Assert.Equal(0u, nativeFilledStruct.Plane);
        Assert.Equal(42, nativeFilledStruct.Fd);
    }

    [Fact]
    public void TestV4L2DecoderCmd_NativeSizeCompatibility()
    {
        // Test that our V4L2DecoderCmd structure has the same size as the native v4l2_decoder_cmd structure
        int csharpSize = Marshal.SizeOf<V4L2DecoderCmd>();
        int nativeSize = NativeTestLibrary.GetNativeV4L2DecoderCmdSize();

        Assert.Equal(nativeSize, csharpSize);
    }

    [Fact]
    public void TestV4L2DecoderCmd_NativeMemoryLayoutCompatibility()
    {
        // Fill C structure in native code and check that managed structure fields have right values
        var nativeFilledStruct = new V4L2DecoderCmd();

        // Fill structure using native C code
        NativeTestLibrary.FillNativeV4L2DecoderCmd(&nativeFilledStruct);

        // Verify that the managed structure fields have the expected values
        Assert.Equal((uint)V4L2DecoderCommand.START, nativeFilledStruct.Cmd);
        Assert.Equal(0u, nativeFilledStruct.Flags);
    }
}

