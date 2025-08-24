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
        Assert.Equal(11u, nativeFilledStruct.ConnectorType); // DRM_MODE_CONNECTOR_HDMIA
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
        Assert.Equal(0x5u, nativeFilledStruct.Flags); // DRM_MODE_FLAG_NHSYNC | DRM_MODE_FLAG_NVSYNC
        Assert.Equal(0x40u, nativeFilledStruct.Type); // DRM_MODE_TYPE_PREFERRED
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
}