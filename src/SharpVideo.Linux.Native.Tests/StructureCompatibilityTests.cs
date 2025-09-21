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

        // Verify that the managed structure fields have the expected distinctive patterns
        Assert.Equal(0xDEAD, nativeFilledStruct.CountFbs);
        Assert.Equal(0xBEEF, nativeFilledStruct.CountCrtcs);
        Assert.Equal(0xCAFE, nativeFilledStruct.CountConnectors);
        Assert.Equal(0xBABE, nativeFilledStruct.CountEncoders);
        Assert.Equal(0x12345678, nativeFilledStruct.MinWidth);
        Assert.Equal(unchecked((int)0x87654321), nativeFilledStruct.MaxWidth);
        Assert.Equal(unchecked((int)0xFEDCBA98), nativeFilledStruct.MinHeight);
        Assert.Equal(unchecked((int)0x89ABCDEF), nativeFilledStruct.MaxHeight);
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

        // Verify that the managed structure fields have the expected distinctive patterns
        Assert.Equal(0xDEADBEEFu, nativeFilledStruct.EncoderId);
        Assert.Equal((DrmModeEncoderType)0xCAFE, nativeFilledStruct.EncoderType);
        Assert.Equal(0xBABEFACEu, nativeFilledStruct.CrtcId);
        Assert.Equal(0x12345678u, nativeFilledStruct.PossibleCrtcs);
        Assert.Equal(0x87654321u, nativeFilledStruct.PossibleClones);
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

        // Verify that the managed structure fields have the expected distinctive patterns
        Assert.Equal(0xFEEDFACEu, nativeFilledStruct.ConnectorId);
        Assert.Equal(0xDEADBEEFu, nativeFilledStruct.EncoderId);
        Assert.Equal((ConnectorType)0xCAFEBABE, nativeFilledStruct.ConnectorType);
        Assert.Equal(0x12345u, nativeFilledStruct.ConnectorTypeId);
        Assert.Equal((DrmModeConnection)0xABCD, nativeFilledStruct.Connection);
        Assert.Equal(0x11223344u, nativeFilledStruct.MmWidth);
        Assert.Equal(0x55667788u, nativeFilledStruct.MmHeight);
        Assert.Equal((DrmModeSubPixel)0x9900AABB, nativeFilledStruct.SubPixel);
        Assert.Equal(unchecked((int)0xCCDDEEFF), nativeFilledStruct.CountModes);
        Assert.Equal(unchecked((int)0x13579BDF), nativeFilledStruct.CountProps);
        Assert.Equal(unchecked((int)0x2468ACE0), nativeFilledStruct.CountEncoders);
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

        // Verify that the managed structure fields have the expected distinctive patterns
        Assert.Equal(0xDEADBEEFu, nativeFilledStruct.CrtcId);
        Assert.Equal(0xCAFEBABEu, nativeFilledStruct.BufferId);
        Assert.Equal(0x12345678u, nativeFilledStruct.X);
        Assert.Equal(0x87654321u, nativeFilledStruct.Y);
        Assert.Equal(0xFEDCBA98u, nativeFilledStruct.Width);
        Assert.Equal(0x89ABCDEFu, nativeFilledStruct.Height);
        Assert.Equal(unchecked((int)0xABCDEF01), nativeFilledStruct.ModeValid);
        Assert.Equal(unchecked((int)0xEEEEEEEE), nativeFilledStruct.GammaSize);

        // Verify embedded mode info with distinctive patterns
        Assert.Equal(0x12345678u, nativeFilledStruct.Mode.Clock);
        Assert.Equal(0x1111, nativeFilledStruct.Mode.HDisplay);
        Assert.Equal(0x6666, nativeFilledStruct.Mode.VDisplay);
        Assert.Equal(0xBBBBu, nativeFilledStruct.Mode.VRefresh);
        Assert.Equal("TEST_MODE_PATTERN_12345", nativeFilledStruct.Mode.NameString);
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

        // Verify that the managed structure fields have the expected distinctive patterns
        Assert.Equal(0x11111111u, nativeFilledStruct.Clock);
        Assert.Equal(0x2222, nativeFilledStruct.HDisplay);
        Assert.Equal(0x3333, nativeFilledStruct.HSyncStart);
        Assert.Equal(0x4444, nativeFilledStruct.HSyncEnd);
        Assert.Equal(0x5555, nativeFilledStruct.HTotal);
        Assert.Equal(0x6666, nativeFilledStruct.HSkew);
        Assert.Equal(0x7777, nativeFilledStruct.VDisplay);
        Assert.Equal(0x8888, nativeFilledStruct.VSyncStart);
        Assert.Equal(0x9999, nativeFilledStruct.VSyncEnd);
        Assert.Equal(0xAAAA, nativeFilledStruct.VTotal);
        Assert.Equal(0xBBBB, nativeFilledStruct.VScan);
        Assert.Equal(0xCCCCu, nativeFilledStruct.VRefresh);
        Assert.Equal((DrmModeFlag)0xDDDDDDDD, nativeFilledStruct.Flags);
        Assert.Equal((DrmModeType)0xEEEEEEEE, nativeFilledStruct.Type);
        Assert.Equal("TEST_MODE_INFO_ABCDEF", nativeFilledStruct.NameString);
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

        // Verify that the managed structure fields have the expected distinctive patterns
        Assert.Equal(0xDEADC0DEu, nativeFilledStruct.CountFormats);
        Assert.Equal(0xFEEDBEEFu, nativeFilledStruct.PlaneId);
        Assert.Equal(0xCAFED00Du, nativeFilledStruct.CrtcId);
        Assert.Equal(0xBADDCAFEu, nativeFilledStruct.FbId);
        Assert.Equal(0x12121212u, nativeFilledStruct.CrtcX);
        Assert.Equal(0x34343434u, nativeFilledStruct.CrtcY);
        Assert.Equal(0x56565656u, nativeFilledStruct.X);
        Assert.Equal(0x78787878u, nativeFilledStruct.Y);
        Assert.Equal(0x9ABCDEF0u, nativeFilledStruct.PossibleCrtcs);
        Assert.Equal(0x13579BDFu, nativeFilledStruct.GammaSize);
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

        // Verify that the managed structure fields have the expected distinctive patterns
        Assert.Equal(0xFACADE00u, nativeFilledStruct.FbId);
        Assert.Equal(0xDEADBEEFu, nativeFilledStruct.Width);
        Assert.Equal(0xCAFEBABEu, nativeFilledStruct.Height);
        Assert.Equal(0x12345678u, nativeFilledStruct.Pitch);
        Assert.Equal(0x87654321u, nativeFilledStruct.Bpp);
        Assert.Equal(0xFEDCBA98u, nativeFilledStruct.Depth);
        Assert.Equal(0x89ABCDEFu, nativeFilledStruct.Handle);
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

        // Verify that the managed structure fields have the expected distinctive patterns
        Assert.Equal(0xDEADBEEFCAFEBABEul, nativeFilledStruct.len);
        Assert.Equal(0x12345678u, nativeFilledStruct.fd);
        Assert.Equal(0x87654321u, nativeFilledStruct.fd_flags);
        Assert.Equal(0xFEDCBA98ul, nativeFilledStruct.heap_flags);
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

        // Verify that the managed structure fields have the expected distinctive patterns
        Assert.Equal("TEST_DRV_DEAD", nativeFilledStruct.DriverString);
        Assert.Equal("TEST_CARD_CAFE", nativeFilledStruct.CardString);
        Assert.Equal("TEST_BUS_12345", nativeFilledStruct.BusInfoString);
        Assert.Equal(0xDEADBEEFu, nativeFilledStruct.Version);
        Assert.Equal(0xCAFEBABEu, nativeFilledStruct.Capabilities);
        Assert.Equal((V4L2Capabilities)0x12345678, nativeFilledStruct.DeviceCaps);
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

        // Verify that the managed structure fields have the expected distinctive patterns
        Assert.Equal(0xDEADBEEFu, nativeFilledStruct.Width);
        Assert.Equal(0xCAFEBABEu, nativeFilledStruct.Height);
        Assert.Equal(0x12345678u, nativeFilledStruct.PixelFormat);
        Assert.Equal(0x87654321u, nativeFilledStruct.Field);
        Assert.Equal(0xAB, nativeFilledStruct.NumPlanes);

        // Check plane format information with distinctive patterns
        var planeFormats = nativeFilledStruct.PlaneFormats;
        Assert.Equal(0xDEADC0DEu, planeFormats[0].SizeImage);
        Assert.Equal(0xFEEDFACEu, planeFormats[0].BytesPerLine);
        Assert.Equal(0xBADDCAFEu, planeFormats[1].SizeImage);
        Assert.Equal(0x13579BDFu, planeFormats[1].BytesPerLine);
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

        // Verify that the managed structure fields have the expected distinctive patterns
        Assert.Equal(0xFACADE00u, nativeFilledStruct.Type);
        Assert.Equal(0xDEADBEEFu, nativeFilledStruct.Pix_mp.Width);
        Assert.Equal(0xCAFEBABEu, nativeFilledStruct.Pix_mp.Height);
        Assert.Equal(0x12345678u, nativeFilledStruct.Pix_mp.PixelFormat);
        Assert.Equal(0xAB, nativeFilledStruct.Pix_mp.NumPlanes);
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

        // Verify that the managed structure fields have the expected distinctive patterns
        Assert.Equal(0xDEADBEEFu, nativeFilledStruct.Count);
        Assert.Equal(0xCAFEBABEu, nativeFilledStruct.Type);
        Assert.Equal(0x12345678u, nativeFilledStruct.Memory);
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

        // Verify that the managed structure fields have the expected distinctive patterns
        Assert.Equal(0xDEADBEEFu, nativeFilledStruct.Index);
        Assert.Equal(0xCAFEBABEu, nativeFilledStruct.Type);
        Assert.Equal(0xFEDCBA98u, nativeFilledStruct.Field);
        Assert.Equal(0x11111111L, nativeFilledStruct.Timestamp.TvSec);
        Assert.Equal(0x22222222L, nativeFilledStruct.Timestamp.TvUsec);
        Assert.Equal(0x33333333u, nativeFilledStruct.Sequence);
        Assert.Equal(0x44444444u, nativeFilledStruct.Memory);
        Assert.Equal(0x55555555u, nativeFilledStruct.Length);
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

        // Verify that the managed structure fields have the expected distinctive patterns
        Assert.Equal(0xDEADBEEFu, nativeFilledStruct.Type);
        Assert.Equal(0xCAFEBABEu, nativeFilledStruct.Index);
        Assert.Equal(0x12345678u, nativeFilledStruct.Plane);
        Assert.Equal(unchecked((int)0xFEDCBA98), nativeFilledStruct.Fd);
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

        // Verify that the managed structure fields have the expected distinctive patterns
        Assert.Equal(0xDEADBEEFu, nativeFilledStruct.Cmd);
        Assert.Equal(0xCAFEBABEu, nativeFilledStruct.Flags);
    }

    [Fact]
    public void TestTimeVal_StructSize()
    {
        // Test that TimeVal structure has expected size (2 * sizeof(long))
        int csharpSize = Marshal.SizeOf<TimeVal>();
        int expectedSize = 16; // 2 * 8 bytes on 64-bit platforms

        Assert.Equal(expectedSize, csharpSize);
    }

    [Fact]
    public void TestTimeVal_FieldLayout()
    {
        // Test that TimeVal fields can be set and read correctly
        var timeVal = new TimeVal
        {
            TvSec = 12345,
            TvUsec = 67890
        };

        Assert.Equal(12345, timeVal.TvSec);
        Assert.Equal(67890, timeVal.TvUsec);
    }

    [Fact]
    public void TestV4L2Fract_StructSize()
    {
        // Test that V4L2Fract structure has expected size (2 * sizeof(uint))
        int csharpSize = Marshal.SizeOf<V4L2Fract>();
        int expectedSize = 8; // 2 * 4 bytes

        Assert.Equal(expectedSize, csharpSize);
    }

    [Fact]
    public void TestV4L2Fract_FieldLayout()
    {
        // Test that V4L2Fract fields can be set and read correctly
        var fract = new V4L2Fract
        {
            Numerator = 30,
            Denominator = 1
        };

        Assert.Equal(30u, fract.Numerator);
        Assert.Equal(1u, fract.Denominator);
    }

    [Fact]
    public void TestIoctlResult_BasicProperties()
    {
        // Test IoctlResult structure basic properties
        var successResult = new IoctlResult(true, 0, "Success");
        var failureResult = new IoctlResult(false, -1, "Error");

        Assert.True(successResult.Success);
        Assert.Equal(0, successResult.ErrorCode);
        Assert.Equal("Success", successResult.ErrorMessage);

        Assert.False(failureResult.Success);
        Assert.Equal(-1, failureResult.ErrorCode);
        Assert.Equal("Error", failureResult.ErrorMessage);
    }

    [Fact]
    public void TestIoctlResultWithDetails_BasicProperties()
    {
        // Test IoctlResultWithDetails structure basic properties
        var successResult = IoctlResultWithDetails.CreateSuccess("test_operation");
        var failureResult = IoctlResultWithDetails.CreateError("test_operation", -1, "Test error", "Test suggestion");

        Assert.True(successResult.Success);
        Assert.Equal("test_operation", successResult.OperationName);
        Assert.Equal(0, successResult.ErrorCode);

        Assert.False(failureResult.Success);
        Assert.Equal("test_operation", failureResult.OperationName);
        Assert.Equal(-1, failureResult.ErrorCode);
        Assert.Equal("Test error", failureResult.ErrorMessage);
        Assert.Equal("Test suggestion", failureResult.ErrorSuggestion);
    }
}

