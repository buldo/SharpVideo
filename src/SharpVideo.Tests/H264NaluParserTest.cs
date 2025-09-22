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
    public void Should_Parse_NALU_Type_Correctly_WithStartCode(byte naluHeader, H264NaluType expectedType)
    {
        // Arrange
        var parser = new H264NaluParser(NaluMode.WithStartCode);
        var naluData = new byte[] { 0x00, 0x00, 0x00, 0x01, naluHeader, 0x42, 0x00, 0x1E }; // Annex-B format

        // Act
        var actualType = parser.GetNaluType(naluData);

        // Assert
        Assert.Equal(expectedType, actualType);
    }

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
    public void Should_Parse_NALU_Type_Correctly_WithoutStartCode(byte naluHeader, H264NaluType expectedType)
    {
        // Arrange
        var parser = new H264NaluParser(NaluMode.WithoutStartCode);
        var naluData = new byte[] { naluHeader, 0x42, 0x00, 0x1E }; // Raw NALU format

        // Act
        var actualType = parser.GetNaluType(naluData);

        // Assert
        Assert.Equal(expectedType, actualType);
    }

    [Fact]
    public void Should_Return_Unspecified_For_Empty_NALU()
    {
        // Arrange
        var parserWithStartCode = new H264NaluParser(NaluMode.WithStartCode);
        var parserWithoutStartCode = new H264NaluParser(NaluMode.WithoutStartCode);
        var emptyNalu = ReadOnlySpan<byte>.Empty;

        // Act & Assert
        Assert.Equal(H264NaluType.Unspecified, parserWithStartCode.GetNaluType(emptyNalu));
        Assert.Equal(H264NaluType.Unspecified, parserWithoutStartCode.GetNaluType(emptyNalu));
    }

    [Fact]
    public void Should_Extract_NALU_Type_Ignoring_Other_Header_Bits_WithStartCode()
    {
        // Arrange - Test that forbidden_zero_bit and nal_ref_idc don't affect type extraction
        var parser = new H264NaluParser(NaluMode.WithStartCode);
        var naluWithDifferentRefIdc = new byte[]
        {
            0x00, 0x00, 0x00, 0x01, // Start code
            0xE7, // forbidden_zero_bit=1 (invalid), nal_ref_idc=3, nal_unit_type=7 (SPS)
            0x42, 0x00, 0x1E
        };

        // Act
        var naluType = parser.GetNaluType(naluWithDifferentRefIdc);

        // Assert
        Assert.Equal(H264NaluType.SequenceParameterSet, naluType); // Should still be SPS (type 7)
    }

    [Fact]
    public void Should_Extract_NALU_Type_Ignoring_Other_Header_Bits_WithoutStartCode()
    {
        // Arrange
        var parser = new H264NaluParser(NaluMode.WithoutStartCode);
        var naluWithDifferentRefIdc = new byte[]
        {
            0xE7, // forbidden_zero_bit=1 (invalid), nal_ref_idc=3, nal_unit_type=7 (SPS)
            0x42, 0x00, 0x1E
        };

        // Act
        var naluType = parser.GetNaluType(naluWithDifferentRefIdc);

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
    public void Should_Parse_All_NALU_Types_WithoutStartCode(int typeValue, H264NaluType expectedType)
    {
        // Arrange
        var parser = new H264NaluParser(NaluMode.WithoutStartCode);
        var naluHeader = (byte)typeValue;
        var naluData = new byte[] { naluHeader, 0x00 };

        // Act
        var actualType = parser.GetNaluType(naluData);

        // Assert
        Assert.Equal(expectedType, actualType);
        Assert.Equal(typeValue, (int)actualType);
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
    public void Should_Parse_All_NALU_Types_WithStartCode(int typeValue, H264NaluType expectedType)
    {
        // Arrange
        var parser = new H264NaluParser(NaluMode.WithStartCode);
        var naluHeader = (byte)typeValue;
        var naluData = new byte[] { 0x00, 0x00, 0x00, 0x01, naluHeader, 0x00 };

        // Act
        var actualType = parser.GetNaluType(naluData);

        // Assert
        Assert.Equal(expectedType, actualType);
        Assert.Equal(typeValue, (int)actualType);
    }

    [Fact]
    public void Should_Handle_3Byte_StartCode()
    {
        // Arrange
        var parser = new H264NaluParser(NaluMode.WithStartCode);
        var naluData = new byte[] { 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1E }; // 3-byte start code

        // Act
        var naluType = parser.GetNaluType(naluData);

        // Assert
        Assert.Equal(H264NaluType.SequenceParameterSet, naluType);
    }

    [Fact]
    public void Should_Validate_Format_WithStartCode()
    {
        // Arrange
        var parser = new H264NaluParser(NaluMode.WithStartCode);
        
        var validNalu4Byte = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x67, 0x42 };
        var validNalu3Byte = new byte[] { 0x00, 0x00, 0x01, 0x67, 0x42 };
        var invalidNalu = new byte[] { 0x67, 0x42, 0x00, 0x1E }; // No start code
        var emptyNalu = new byte[0];

        // Act & Assert
        Assert.True(parser.HasValidFormat(validNalu4Byte));
        Assert.True(parser.HasValidFormat(validNalu3Byte));
        Assert.False(parser.HasValidFormat(invalidNalu));
        Assert.False(parser.HasValidFormat(emptyNalu));
    }

    [Fact]
    public void Should_Validate_Format_WithoutStartCode()
    {
        // Arrange
        var parser = new H264NaluParser(NaluMode.WithoutStartCode);
        
        var validNalu = new byte[] { 0x67, 0x42, 0x00, 0x1E };
        var emptyNalu = new byte[0];

        // Act & Assert
        Assert.True(parser.HasValidFormat(validNalu));
        Assert.False(parser.HasValidFormat(emptyNalu));
    }

    [Fact]
    public void Should_Extract_NALU_Payload_WithStartCode()
    {
        // Arrange
        var parser = new H264NaluParser(NaluMode.WithStartCode);
        var expectedPayload = new byte[] { 0x67, 0x42, 0x00, 0x1E };
        var naluData = new byte[] { 0x00, 0x00, 0x00, 0x01 }.Concat(expectedPayload).ToArray();

        // Act
        var payload = parser.GetNaluPayload(naluData);

        // Assert
        Assert.True(payload.SequenceEqual(expectedPayload));
    }

    [Fact]
    public void Should_Extract_NALU_Payload_WithoutStartCode()
    {
        // Arrange
        var parser = new H264NaluParser(NaluMode.WithoutStartCode);
        var expectedPayload = new byte[] { 0x67, 0x42, 0x00, 0x1E };

        // Act
        var payload = parser.GetNaluPayload(expectedPayload);

        // Assert
        Assert.True(payload.SequenceEqual(expectedPayload));
    }

    [Fact]
    public void Should_Parse_NALU_Header_Fields()
    {
        // Arrange
        var parser = new H264NaluParser(NaluMode.WithoutStartCode);
        var naluData = new byte[] { 0x67, 0x42, 0x00, 0x1E }; // nal_ref_idc=3, type=7

        // Act
        var (forbiddenZeroBit, nalRefIdc, naluType) = parser.ParseNaluHeader(naluData);

        // Assert
        Assert.Equal(0, forbiddenZeroBit);
        Assert.Equal(3, nalRefIdc);
        Assert.Equal(H264NaluType.SequenceParameterSet, naluType);
    }

    [Fact]
    public void Should_Get_NALU_Header_Byte()
    {
        // Arrange
        var parserWithStartCode = new H264NaluParser(NaluMode.WithStartCode);
        var parserWithoutStartCode = new H264NaluParser(NaluMode.WithoutStartCode);
        
        var naluWithStartCode = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1E };
        var naluWithoutStartCode = new byte[] { 0x67, 0x42, 0x00, 0x1E };

        // Act
        var headerWithStartCode = parserWithStartCode.GetNaluHeader(naluWithStartCode);
        var headerWithoutStartCode = parserWithoutStartCode.GetNaluHeader(naluWithoutStartCode);

        // Assert
        Assert.Equal(0x67, headerWithStartCode);
        Assert.Equal(0x67, headerWithoutStartCode);
    }
}