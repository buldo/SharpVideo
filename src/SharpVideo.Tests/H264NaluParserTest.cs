using SharpVideo.H264;

namespace SharpVideo.Tests;

public class H264NaluParserTest
{
    [Theory]
    [InlineData(0x01, H264NaluType.CodedSliceNonIdr)]
    [InlineData(0x05, H264NaluType.CodedSliceIdr)]
    [InlineData(0x06, H264NaluType.SupplementalEnhancementInformation)]
    [InlineData(0x07, H264NaluType.SequenceParameterSet)]
    [InlineData(0x08, H264NaluType.PictureParameterSet)]
    [InlineData(0x09, H264NaluType.AccessUnitDelimiter)]
    [InlineData(0x0A, H264NaluType.EndOfSequence)]
    [InlineData(0x0B, H264NaluType.EndOfStream)]
    [InlineData(0x0C, H264NaluType.FillerData)]
    [InlineData(0x67, H264NaluType.SequenceParameterSet)] // SPS with nal_ref_idc = 3
    [InlineData(0x68, H264NaluType.PictureParameterSet)]  // PPS with nal_ref_idc = 3
    [InlineData(0x65, H264NaluType.CodedSliceIdr)]        // IDR slice with nal_ref_idc = 3
    [InlineData(0x41, H264NaluType.CodedSliceNonIdr)]     // Non-IDR slice with nal_ref_idc = 2
    public void Should_Parse_NALU_Type_Correctly(byte naluHeader, H264NaluType expectedType)
    {
        // Arrange
        var naluData = new byte[] { naluHeader, 0x42, 0x00, 0x1E }; // Sample NALU with header

        // Act
        var actualType = H264NaluParser.GetNaluType(naluData);

        // Assert
        Assert.Equal(expectedType, actualType);
    }

    [Fact]
    public void Should_Return_Unspecified_For_Empty_NALU()
    {
        // Arrange
        var emptyNalu = ReadOnlySpan<byte>.Empty;

        // Act
        var naluType = H264NaluParser.GetNaluType(emptyNalu);

        // Assert
        Assert.Equal(H264NaluType.Unspecified, naluType);
    }

    [Fact]
    public void Should_Extract_NALU_Type_Ignoring_Other_Header_Bits()
    {
        // Arrange - Test that forbidden_zero_bit and nal_ref_idc don't affect type extraction
        var naluWithDifferentRefIdc = new byte[]
        {
            0xE7, // forbidden_zero_bit=1 (invalid), nal_ref_idc=3, nal_unit_type=7 (SPS)
            0x42, 0x00, 0x1E
        };

        // Act
        var naluType = H264NaluParser.GetNaluType(naluWithDifferentRefIdc);

        // Assert
        Assert.Equal(H264NaluType.SequenceParameterSet, naluType); // Should still be SPS (type 7)
    }

    [Theory]
    [InlineData(0, H264NaluType.Unspecified)]
    [InlineData(13, H264NaluType.SequenceParameterSetExtension)]
    [InlineData(14, H264NaluType.PrefixNalUnit)]
    [InlineData(15, H264NaluType.SubsetSequenceParameterSet)]
    [InlineData(16, H264NaluType.DepthParameterSet)]
    [InlineData(19, H264NaluType.CodedSliceAuxiliary)]
    [InlineData(20, H264NaluType.CodedSliceExtension)]
    [InlineData(21, H264NaluType.CodedSliceExtensionDepthView)]
    [InlineData(24, H264NaluType.Unspecified24)]
    [InlineData(31, H264NaluType.Unspecified31)]
    public void Should_Parse_All_NALU_Types(int typeValue, H264NaluType expectedType)
    {
        // Arrange
        var naluHeader = (byte)typeValue;
        var naluData = new byte[] { naluHeader, 0x00 };

        // Act
        var actualType = H264NaluParser.GetNaluType(naluData);

        // Assert
        Assert.Equal(expectedType, actualType);
        Assert.Equal(typeValue, (int)actualType);
    }
}