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
}