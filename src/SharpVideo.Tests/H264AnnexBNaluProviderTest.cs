using SharpVideo.H264;
using System.Threading.Channels;

namespace SharpVideo.Tests
{
    public class H264AnnexBNaluProviderTest : IDisposable
    {
        private H264AnnexBNaluProvider? _provider;

        [Fact]
        public async Task Should_Parse_Single_NALU_With_4Byte_StartCode()
        {
            // Arrange
            _provider = new H264AnnexBNaluProvider();
            var naluData = new byte[] { 0x67, 0x42, 0x00, 0x1E }; // Sample SPS NALU data
            var expectedAnnexBNalu = new byte[] { 0x00, 0x00, 0x00, 0x01 }.Concat(naluData).ToArray();
            var inputData = new byte[] { 0x00, 0x00, 0x00, 0x01 }
                .Concat(naluData)
                .Concat(new byte[] { 0x00, 0x00, 0x00, 0x01 }) // Next start code
                .ToArray();

            // Act
            await _provider.AppendData(inputData, CancellationToken.None);
            _provider.CompleteWriting();

            // Assert
            var nalu = await _provider.NaluReader.ReadAsync();
            Assert.Equal(expectedAnnexBNalu, nalu.Data.ToArray());
            Assert.Equal(naluData, nalu.WithoutHeader.ToArray());
        }

        [Fact]
        public async Task Should_Parse_Single_NALU_With_3Byte_StartCode()
        {
            // Arrange
            _provider = new H264AnnexBNaluProvider();
            var naluData = new byte[] { 0x67, 0x42, 0x00, 0x1E }; // Sample SPS NALU data
            var expectedAnnexBNalu = new byte[] { 0x00, 0x00, 0x01 }.Concat(naluData).ToArray();
            var inputData = new byte[] { 0x00, 0x00, 0x01 }
                .Concat(naluData)
                .Concat(new byte[] { 0x00, 0x00, 0x01 }) // Next start code
                .ToArray();

            // Act
            await _provider.AppendData(inputData, CancellationToken.None);
            _provider.CompleteWriting();

            // Assert
            var nalu = await _provider.NaluReader.ReadAsync();
            Assert.Equal(expectedAnnexBNalu, nalu.Data.ToArray());
            Assert.Equal(naluData, nalu.WithoutHeader.ToArray());
        }

        [Fact]
        public async Task Should_Parse_Multiple_NALUs()
        {
            // Arrange
            _provider = new H264AnnexBNaluProvider();
            var nalu1 = new byte[] { 0x67, 0x42, 0x00, 0x1E }; // SPS
            var nalu2 = new byte[] { 0x68, 0xCE, 0x38, 0x80 }; // PPS
            var nalu3 = new byte[] { 0x65, 0x88, 0x84, 0x00 }; // IDR slice

            var expectedNalu1 = new byte[] { 0x00, 0x00, 0x00, 0x01 }.Concat(nalu1).ToArray();
            var expectedNalu2 = new byte[] { 0x00, 0x00, 0x00, 0x01 }.Concat(nalu2).ToArray();
            var expectedNalu3 = new byte[] { 0x00, 0x00, 0x00, 0x01 }.Concat(nalu3).ToArray();

            var inputData = new byte[] { 0x00, 0x00, 0x00, 0x01 }
                .Concat(nalu1)
                .Concat(new byte[] { 0x00, 0x00, 0x00, 0x01 })
                .Concat(nalu2)
                .Concat(new byte[] { 0x00, 0x00, 0x00, 0x01 })
                .Concat(nalu3)
                .ToArray();

            // Act
            await _provider.AppendData(inputData, CancellationToken.None);
            _provider.CompleteWriting();

            // Assert
            var nalus = new List<H264Nalu>();
            await foreach (var nalu in _provider.NaluReader.ReadAllAsync())
            {
                nalus.Add(nalu);
            }

            Assert.Equal(3, nalus.Count);
            Assert.Equal(expectedNalu1, nalus[0].Data.ToArray());
            Assert.Equal(expectedNalu2, nalus[1].Data.ToArray());
            Assert.Equal(expectedNalu3, nalus[2].Data.ToArray());
            
            // Verify that WithoutHeader returns just the payload
            Assert.Equal(nalu1, nalus[0].WithoutHeader.ToArray());
            Assert.Equal(nalu2, nalus[1].WithoutHeader.ToArray());
            Assert.Equal(nalu3, nalus[2].WithoutHeader.ToArray());
        }

        [Fact]
        public async Task Should_Handle_Fragmented_Data()
        {
            // Arrange
            _provider = new H264AnnexBNaluProvider();
            var naluData = new byte[] { 0x67, 0x42, 0x00, 0x1E, 0xA9, 0x50, 0x14, 0x07 };
            var expectedAnnexBNalu = new byte[] { 0x00, 0x00, 0x00, 0x01 }.Concat(naluData).ToArray();
            var startCode = new byte[] { 0x00, 0x00, 0x00, 0x01 };
            var fragment1 = startCode.Take(2).ToArray();
            var fragment2 = startCode.Skip(2).Concat(naluData.Take(3)).ToArray();
            var fragment3 = naluData.Skip(3).Concat(new byte[] { 0x00, 0x00, 0x00, 0x01 }).ToArray();

            // Act
            await _provider.AppendData(fragment1, CancellationToken.None);
            await _provider.AppendData(fragment2, CancellationToken.None);
            await _provider.AppendData(fragment3, CancellationToken.None);
            _provider.CompleteWriting();

            // Assert
            var nalu = await _provider.NaluReader.ReadAsync();
            Assert.Equal(expectedAnnexBNalu, nalu.Data.ToArray());
            Assert.Equal(naluData, nalu.WithoutHeader.ToArray());
        }

        [Fact]
        public async Task Should_Handle_Mixed_StartCode_Types()
        {
            // Arrange
            _provider = new H264AnnexBNaluProvider();
            var nalu1 = new byte[] { 0x67, 0x42, 0x00, 0x1E };
            var nalu2 = new byte[] { 0x68, 0xCE, 0x38, 0x80 };

            var expectedNalu1 = new byte[] { 0x00, 0x00, 0x00, 0x01 }.Concat(nalu1).ToArray();
            var expectedNalu2 = new byte[] { 0x00, 0x00, 0x01 }.Concat(nalu2).ToArray();

            var inputData = new byte[] { 0x00, 0x00, 0x00, 0x01 } // 4-byte start code
                .Concat(nalu1)
                .Concat(new byte[] { 0x00, 0x00, 0x01 }) // 3-byte start code
                .Concat(nalu2)
                .Concat(new byte[] { 0x00, 0x00, 0x00, 0x01 }) // 4-byte start code
                .ToArray();

            // Act
            await _provider.AppendData(inputData, CancellationToken.None);
            _provider.CompleteWriting();

            // Assert
            var nalus = new List<H264Nalu>();
            await foreach (var nalu in _provider.NaluReader.ReadAllAsync())
            {
                nalus.Add(nalu);
            }

            Assert.Equal(2, nalus.Count);
            Assert.Equal(expectedNalu1, nalus[0].Data.ToArray());
            Assert.Equal(expectedNalu2, nalus[1].Data.ToArray());
            
            // Verify that WithoutHeader returns just the payload regardless of start code type
            Assert.Equal(nalu1, nalus[0].WithoutHeader.ToArray());
            Assert.Equal(nalu2, nalus[1].WithoutHeader.ToArray());
        }

        [Fact]
        public async Task Should_Handle_Empty_Data()
        {
            // Arrange
            _provider = new H264AnnexBNaluProvider();

            // Act
            await _provider.AppendData(Array.Empty<byte>(), CancellationToken.None);
            _provider.CompleteWriting();

            // Assert - The channel should be completed without any NALUs
            var nalus = new List<H264Nalu>();
            await foreach (var nalu in _provider.NaluReader.ReadAllAsync())
            {
                nalus.Add(nalu);
            }
            Assert.Empty(nalus);
        }

        [Fact]
        public async Task Should_Handle_Data_Without_StartCodes()
        {
            // Arrange
            _provider = new H264AnnexBNaluProvider();
            var dataWithoutStartCodes = new byte[] { 0x67, 0x42, 0x00, 0x1E, 0xA9, 0x50 };
            var expectedAnnexBNalu = new byte[] { 0x00, 0x00, 0x00, 0x01 }.Concat(dataWithoutStartCodes).ToArray();

            // Act
            await _provider.AppendData(dataWithoutStartCodes, CancellationToken.None);
            _provider.CompleteWriting();

            // Assert - should get the data as a single NALU in Annex-B format when stream completes
            var nalu = await _provider.NaluReader.ReadAsync();
            Assert.Equal(expectedAnnexBNalu, nalu.Data.ToArray());
            Assert.Equal(dataWithoutStartCodes, nalu.WithoutHeader.ToArray());
        }

        [Fact]
        public async Task Should_Validate_NALU_Data_Integrity()
        {
            // Arrange
            var testVideoPath = Path.Combine(Directory.GetCurrentDirectory(), "test_video.h264");
            if (!File.Exists(testVideoPath))
            {
                return;
            }

            var h264Data = await File.ReadAllBytesAsync(testVideoPath);

            // Parse NALUs and verify integrity
            var nalus = await ParseNalus(h264Data);

            // Assert
            Assert.NotEmpty(nalus);

            // Verify that all NALUs have valid structure
            for (int i = 0; i < nalus.Count; i++)
            {
                var nalu = nalus[i];
                
                // Verify that Data always contains start code
                Assert.True(HasStartCode(nalu.Data.ToArray()),
                    $"NALU {i} should always have start code in Data property");
                    
                // Verify that WithoutHeader doesn't contain start code
                Assert.False(HasStartCode(nalu.WithoutHeader.ToArray()),
                    $"NALU {i} should not have start code in WithoutHeader property");
                    
                ValidateNaluFormat(nalu.Data.ToArray(), i);
            }
        }

        private async Task<List<H264Nalu>> ParseNalus(byte[] h264Data)
        {
            using var provider = new H264AnnexBNaluProvider();
            var nalus = new List<H264Nalu>();

            var readTask = Task.Run(async () =>
            {
                await foreach (var nalu in provider.NaluReader.ReadAllAsync())
                {
                    nalus.Add(nalu);
                }
            });

            await provider.AppendData(h264Data, CancellationToken.None);
            provider.CompleteWriting();
            await readTask;

            return nalus;
        }

        private static byte[] ExtractNaluDataWithoutStartCode(byte[] naluWithStartCode)
        {
            // Check for 4-byte start code
            if (naluWithStartCode.Length >= 4 &&
                naluWithStartCode[0] == 0x00 && naluWithStartCode[1] == 0x00 && naluWithStartCode[2] == 0x00 && naluWithStartCode[3] == 0x01)
            {
                return naluWithStartCode[4..];
            }

            // Check for 3-byte start code
            if (naluWithStartCode.Length >= 3 &&
                naluWithStartCode[0] == 0x00 && naluWithStartCode[1] == 0x00 && naluWithStartCode[2] == 0x01)
            {
                return naluWithStartCode[3..];
            }

            // No start code found, return as is
            return naluWithStartCode;
        }

        private static bool HasStartCode(byte[] nalu)
        {
            if (nalu.Length >= 4 &&
                nalu[0] == 0x00 && nalu[1] == 0x00 &&
                nalu[2] == 0x00 && nalu[3] == 0x01)
            {
                return true;
            }

            if (nalu.Length >= 3 &&
                nalu[0] == 0x00 && nalu[1] == 0x00 && nalu[2] == 0x01)
            {
                return true;
            }

            return false;
        }

        private static void ValidateNaluFormat(byte[] nalu, int index)
        {
            Assert.True(nalu.Length >= 4, $"NALU {index} should have at least 4 bytes (start code + data)");

            // Check for valid start code
            bool has4ByteStartCode = nalu.Length >= 4 &&
                nalu[0] == 0x00 && nalu[1] == 0x00 && nalu[2] == 0x00 && nalu[3] == 0x01;
            bool has3ByteStartCode = nalu.Length >= 3 &&
                nalu[0] == 0x00 && nalu[1] == 0x00 && nalu[2] == 0x01;

            Assert.True(has4ByteStartCode || has3ByteStartCode,
                $"NALU {index} should start with valid Annex-B start code");

            // Validate NALU header
            int headerIndex = has4ByteStartCode ? 4 : 3;
            if (headerIndex < nalu.Length)
            {
                var naluHeader = nalu[headerIndex];
                var naluType = naluHeader & 0x1F;
                var forbiddenZeroBit = (naluHeader & 0x80) >> 7;
                var nalRefIdc = (naluHeader & 0x60) >> 5;

                Assert.True(forbiddenZeroBit == 0, $"NALU {index} forbidden_zero_bit should be 0");
                Assert.True(naluType >= 0 && naluType <= 31, $"NALU {index} type {naluType} should be in valid range");
                Assert.True(nalRefIdc >= 0 && nalRefIdc <= 3, $"NALU {index} nal_ref_idc {nalRefIdc} should be in valid range");
            }
        }
        
        public void Dispose()
        {
            _provider?.Dispose();
        }
    }
}
