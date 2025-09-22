using SharpVideo.H264;
using System.Threading.Channels;

namespace SharpVideo.Tests
{
    public class H264NaluProviderTest : IDisposable
    {
        private H264NaluProvider? _provider;

        [Fact]
        public async Task Should_Parse_Single_NALU_With_4Byte_StartCode_WithStartCode()
        {
            // Arrange
            _provider = new H264NaluProvider(NaluMode.WithStartCode);
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
            Assert.Equal(expectedAnnexBNalu, nalu);
        }

        [Fact]
        public async Task Should_Parse_Single_NALU_With_4Byte_StartCode_WithoutStartCode()
        {
            // Arrange
            _provider = new H264NaluProvider(NaluMode.WithoutStartCode);
            var naluData = new byte[] { 0x67, 0x42, 0x00, 0x1E }; // Sample SPS NALU data
            var inputData = new byte[] { 0x00, 0x00, 0x00, 0x01 }
                .Concat(naluData)
                .Concat(new byte[] { 0x00, 0x00, 0x00, 0x01 }) // Next start code
                .ToArray();

            // Act
            await _provider.AppendData(inputData, CancellationToken.None);
            _provider.CompleteWriting();

            // Assert
            var nalu = await _provider.NaluReader.ReadAsync();
            Assert.Equal(naluData, nalu); // Should not include start code
        }

        [Fact]
        public async Task Should_Parse_Single_NALU_With_3Byte_StartCode_WithStartCode()
        {
            // Arrange
            _provider = new H264NaluProvider(NaluMode.WithStartCode);
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
            Assert.Equal(expectedAnnexBNalu, nalu);
        }

        [Fact]
        public async Task Should_Parse_Single_NALU_With_3Byte_StartCode_WithoutStartCode()
        {
            // Arrange
            _provider = new H264NaluProvider(NaluMode.WithoutStartCode);
            var naluData = new byte[] { 0x67, 0x42, 0x00, 0x1E }; // Sample SPS NALU data
            var inputData = new byte[] { 0x00, 0x00, 0x01 }
                .Concat(naluData)
                .Concat(new byte[] { 0x00, 0x00, 0x01 }) // Next start code
                .ToArray();

            // Act
            await _provider.AppendData(inputData, CancellationToken.None);
            _provider.CompleteWriting();

            // Assert
            var nalu = await _provider.NaluReader.ReadAsync();
            Assert.Equal(naluData, nalu); // Should not include start code
        }

        [Fact]
        public async Task Should_Parse_Multiple_NALUs_WithStartCode()
        {
            // Arrange
            _provider = new H264NaluProvider(NaluMode.WithStartCode);
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
            var nalus = new List<byte[]>();
            await foreach (var nalu in _provider.NaluReader.ReadAllAsync())
            {
                nalus.Add(nalu);
            }

            Assert.Equal(3, nalus.Count);
            Assert.Equal(expectedNalu1, nalus[0]);
            Assert.Equal(expectedNalu2, nalus[1]);
            Assert.Equal(expectedNalu3, nalus[2]);
        }

        [Fact]
        public async Task Should_Parse_Multiple_NALUs_WithoutStartCode()
        {
            // Arrange
            _provider = new H264NaluProvider(NaluMode.WithoutStartCode);
            var nalu1 = new byte[] { 0x67, 0x42, 0x00, 0x1E }; // SPS
            var nalu2 = new byte[] { 0x68, 0xCE, 0x38, 0x80 }; // PPS
            var nalu3 = new byte[] { 0x65, 0x88, 0x84, 0x00 }; // IDR slice

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
            var nalus = new List<byte[]>();
            await foreach (var nalu in _provider.NaluReader.ReadAllAsync())
            {
                nalus.Add(nalu);
            }

            Assert.Equal(3, nalus.Count);
            Assert.Equal(nalu1, nalus[0]); // Should not include start code
            Assert.Equal(nalu2, nalus[1]); // Should not include start code
            Assert.Equal(nalu3, nalus[2]); // Should not include start code
        }

        [Fact]
        public async Task Should_Handle_Fragmented_Data()
        {
            // Arrange
            _provider = new H264NaluProvider(NaluMode.WithStartCode);
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
            Assert.Equal(expectedAnnexBNalu, nalu);
        }

        [Fact]
        public async Task Should_Handle_Mixed_StartCode_Types()
        {
            // Arrange
            _provider = new H264NaluProvider(NaluMode.WithStartCode);
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
            var nalus = new List<byte[]>();
            await foreach (var nalu in _provider.NaluReader.ReadAllAsync())
            {
                nalus.Add(nalu);
            }

            Assert.Equal(2, nalus.Count);
            Assert.Equal(expectedNalu1, nalus[0]);
            Assert.Equal(expectedNalu2, nalus[1]);
        }

        [Fact]
        public async Task Should_Handle_Empty_Data()
        {
            // Arrange
            _provider = new H264NaluProvider(NaluMode.WithStartCode);

            // Act
            await _provider.AppendData(Array.Empty<byte>(), CancellationToken.None);
            _provider.CompleteWriting();

            // Assert - The channel should be completed without any NALUs
            var nalus = new List<byte[]>();
            await foreach (var nalu in _provider.NaluReader.ReadAllAsync())
            {
                nalus.Add(nalu);
            }
            Assert.Empty(nalus);
        }

        [Fact]
        public async Task Should_Handle_Data_Without_StartCodes_WithStartCode()
        {
            // Arrange
            _provider = new H264NaluProvider(NaluMode.WithStartCode);
            var dataWithoutStartCodes = new byte[] { 0x67, 0x42, 0x00, 0x1E, 0xA9, 0x50 };
            var expectedAnnexBNalu = new byte[] { 0x00, 0x00, 0x00, 0x01 }.Concat(dataWithoutStartCodes).ToArray();

            // Act
            await _provider.AppendData(dataWithoutStartCodes, CancellationToken.None);
            _provider.CompleteWriting();

            // Assert - should get the data as a single NALU in Annex-B format when stream completes
            var nalu = await _provider.NaluReader.ReadAsync();
            Assert.Equal(expectedAnnexBNalu, nalu);
        }

        [Fact]
        public async Task Should_Handle_Data_Without_StartCodes_WithoutStartCode()
        {
            // Arrange
            _provider = new H264NaluProvider(NaluMode.WithoutStartCode);
            var dataWithoutStartCodes = new byte[] { 0x67, 0x42, 0x00, 0x1E, 0xA9, 0x50 };

            // Act
            await _provider.AppendData(dataWithoutStartCodes, CancellationToken.None);
            _provider.CompleteWriting();

            // Assert - should get the raw data without start code
            var nalu = await _provider.NaluReader.ReadAsync();
            Assert.Equal(dataWithoutStartCodes, nalu);
        }

        [Fact]
        public async Task Should_Parse_Real_H264_File()
        {
            // Arrange
            _provider = new H264NaluProvider(NaluMode.WithStartCode);
            var testVideoPath = Path.Combine(Directory.GetCurrentDirectory(), "test_video.h264");

            if (!File.Exists(testVideoPath))
            {
                // Skip test if file doesn't exist
                return;
            }

            var h264Data = await File.ReadAllBytesAsync(testVideoPath);
            var nalus = new List<byte[]>();

            // Act
            var readTask = Task.Run(async () =>
            {
                await foreach (var nalu in _provider.NaluReader.ReadAllAsync())
                {
                    nalus.Add(nalu);
                }
            });

            await _provider.AppendData(h264Data, CancellationToken.None);
            _provider.CompleteWriting();
            await readTask;

            // Assert
            Assert.NotEmpty(nalus);

            // Enhanced validation
            var naluStats = AnalyzeNalus(nalus);

            // Verify basic requirements
            Assert.True(naluStats.TotalNalus > 0, "Should have parsed at least one NALU");
            Assert.True(naluStats.TotalDataSize > 0, "Total NALU data size should be greater than 0");

            // Verify each NALU has correct format
            foreach (var (nalu, index) in nalus.Select((n, i) => (n, i)))
            {
                ValidateNaluFormat(nalu, index);
            }

            // Verify we have some expected NALU types (checking after start code)
            var spsFound = nalus.Any(nalu =>
            {
                var naluType = GetNaluTypeFromAnnexB(nalu);
                return naluType == H264NaluType.SequenceParameterSet;
            });

            var ppsFound = nalus.Any(nalu =>
            {
                var naluType = GetNaluTypeFromAnnexB(nalu);
                return naluType == H264NaluType.PictureParameterSet;
            });

            var sliceFound = nalus.Any(nalu =>
            {
                var naluType = GetNaluTypeFromAnnexB(nalu);
                return naluType == H264NaluType.CodedSliceNonIdr || naluType == H264NaluType.CodedSliceIdr;
            });

            Assert.True(spsFound, "Should find SPS NALU");
            Assert.True(ppsFound, "Should find PPS NALU");
            Assert.True(sliceFound, "Should find slice NALUs");

            // Verify NALU sizes are reasonable
            Assert.True(naluStats.MinNaluSize >= 5, $"Minimum NALU size should be at least 5 bytes (start code + header), got {naluStats.MinNaluSize}");
            Assert.True(naluStats.MaxNaluSize <= h264Data.Length, $"Maximum NALU size ({naluStats.MaxNaluSize}) should not exceed input data size ({h264Data.Length})");
            Assert.True(naluStats.AverageNaluSize > 0, $"Average NALU size should be greater than 0, got {naluStats.AverageNaluSize:F2}");

            // Verify data integrity - sum of all NALU data should not exceed input
            var totalNaluBytes = nalus.Sum(n => n.Length);
            Assert.True(totalNaluBytes <= h264Data.Length + (nalus.Count * 4),
                $"Total NALU bytes ({totalNaluBytes}) should not significantly exceed input data ({h264Data.Length})");

            // Output statistics for debugging
            Console.WriteLine($"NALU Statistics: {naluStats.TotalNalus} NALUs, {naluStats.SpsCount} SPS, {naluStats.PpsCount} PPS, {naluStats.SliceCount} slices");
        }

        [Fact]
        public async Task Should_Parse_Real_H264_File_WithoutStartCode()
        {
            // Arrange
            _provider = new H264NaluProvider(NaluMode.WithoutStartCode);
            var testVideoPath = Path.Combine(Directory.GetCurrentDirectory(), "test_video.h264");

            if (!File.Exists(testVideoPath))
            {
                return;
            }

            var h264Data = await File.ReadAllBytesAsync(testVideoPath);
            var nalus = new List<byte[]>();

            // Act
            var readTask = Task.Run(async () =>
            {
                await foreach (var nalu in _provider.NaluReader.ReadAllAsync())
                {
                    nalus.Add(nalu);
                }
            });

            await _provider.AppendData(h264Data, CancellationToken.None);
            _provider.CompleteWriting();
            await readTask;

            // Assert
            Assert.NotEmpty(nalus);

            // Verify NALUs don't have start codes
            foreach (var (nalu, index) in nalus.Select((n, i) => (n, i)))
            {
                Assert.True(nalu.Length >= 1, $"NALU {index} should have at least 1 byte (header)");

                // Should not start with start code patterns
                if (nalu.Length >= 4)
                {
                    bool has4ByteStartCode = nalu[0] == 0x00 && nalu[1] == 0x00 &&
                                           nalu[2] == 0x00 && nalu[3] == 0x01;
                    Assert.False(has4ByteStartCode, $"NALU {index} should not start with 4-byte start code");
                }

                if (nalu.Length >= 3)
                {
                    bool has3ByteStartCode = nalu[0] == 0x00 && nalu[1] == 0x00 && nalu[2] == 0x01;
                    Assert.False(has3ByteStartCode, $"NALU {index} should not start with 3-byte start code");
                }

                // Verify NALU type is valid (should be able to extract from first byte)
                var naluType = nalu[0] & 0x1F;
                Assert.True(naluType >= 0 && naluType <= 31, $"NALU {index} type {naluType} should be in valid range 0-31");
            }

            var naluStats = AnalyzeRawNalus(nalus);
            Console.WriteLine($"Raw NALU Statistics: {naluStats.TotalNalus} NALUs, {naluStats.SpsCount} SPS, {naluStats.PpsCount} PPS, {naluStats.SliceCount} slices");
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

            // Test both modes and compare results
            var nalusWithStartCode = await ParseNalusWithMode(h264Data, NaluMode.WithStartCode);
            var nalusWithoutStartCode = await ParseNalusWithMode(h264Data, NaluMode.WithoutStartCode);

            // Assert
            Assert.Equal(nalusWithStartCode.Count, nalusWithoutStartCode.Count);

            // Additional message for debugging
            if (nalusWithStartCode.Count != nalusWithoutStartCode.Count)
            {
                throw new Exception("Both modes should produce the same number of NALUs");
            }

            // Verify that WithStartCode mode includes start codes and WithoutStartCode doesn't
            for (int i = 0; i < nalusWithStartCode.Count; i++)
            {
                var naluWithStartCode = nalusWithStartCode[i];
                var naluWithoutStartCode = nalusWithoutStartCode[i];

                // Extract NALU data without start code from the WithStartCode version
                var extractedNaluData = ExtractNaluDataWithoutStartCode(naluWithStartCode);

                // Should be identical to WithoutStartCode version
                Assert.True(extractedNaluData.SequenceEqual(naluWithoutStartCode),
                    $"NALU {i} data should be identical when start code is stripped");

                // Verify start code presence
                Assert.True(HasStartCode(naluWithStartCode),
                    $"NALU {i} with start code mode should have start code");
                Assert.False(HasStartCode(naluWithoutStartCode),
                    $"NALU {i} without start code mode should not have start code");
            }
        }

        private async Task<List<byte[]>> ParseNalusWithMode(byte[] h264Data, NaluMode mode)
        {
            using var provider = new H264NaluProvider(mode);
            var nalus = new List<byte[]>();

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

        private static NaluStatistics AnalyzeNalus(List<byte[]> nalus)
        {
            var stats = new NaluStatistics();
            stats.TotalNalus = nalus.Count;
            
            if (nalus.Count == 0) return stats;
            
            var sizes = new List<int>();
            
            foreach (var nalu in nalus)
            {
                sizes.Add(nalu.Length);
                stats.TotalDataSize += nalu.Length;
                
                var naluType = GetNaluTypeFromAnnexB(nalu);
                switch (naluType)
                {
                    case H264NaluType.SequenceParameterSet: stats.SpsCount++; break;
                    case H264NaluType.PictureParameterSet: stats.PpsCount++; break;
                    case H264NaluType.CodedSliceNonIdr:
                    case H264NaluType.CodedSliceDataPartitionA:
                    case H264NaluType.CodedSliceDataPartitionB:
                    case H264NaluType.CodedSliceDataPartitionC:
                    case H264NaluType.CodedSliceIdr: stats.SliceCount++; break;
                    case H264NaluType.SupplementalEnhancementInformation: stats.SeiCount++; break;
                    case H264NaluType.AccessUnitDelimiter: stats.AudCount++; break;
                    default: stats.OtherCount++; break;
                }
            }
            
            stats.MinNaluSize = sizes.Min();
            stats.MaxNaluSize = sizes.Max();
            stats.AverageNaluSize = sizes.Average();
            
            return stats;
        }

        private static NaluStatistics AnalyzeRawNalus(List<byte[]> nalus)
        {
            var stats = new NaluStatistics();
            stats.TotalNalus = nalus.Count;
            
            if (nalus.Count == 0) return stats;
            
            var sizes = new List<int>();
            
            foreach (var nalu in nalus)
            {
                sizes.Add(nalu.Length);
                stats.TotalDataSize += nalu.Length;
                
                if (nalu.Length > 0)
                {
                    var naluType = H264NaluParser.GetNaluType(nalu);
                    switch (naluType)
                    {
                        case H264NaluType.SequenceParameterSet: stats.SpsCount++; break;
                        case H264NaluType.PictureParameterSet: stats.PpsCount++; break;
                        case H264NaluType.CodedSliceNonIdr:
                        case H264NaluType.CodedSliceDataPartitionA:
                        case H264NaluType.CodedSliceDataPartitionB:
                        case H264NaluType.CodedSliceDataPartitionC:
                        case H264NaluType.CodedSliceIdr: stats.SliceCount++; break;
                        case H264NaluType.SupplementalEnhancementInformation: stats.SeiCount++; break;
                        case H264NaluType.AccessUnitDelimiter: stats.AudCount++; break;
                        default: stats.OtherCount++; break;
                    }
                }
            }
            
            stats.MinNaluSize = sizes.Min();
            stats.MaxNaluSize = sizes.Max();
            stats.AverageNaluSize = sizes.Average();
            
            return stats;
        }

        // Helper method to extract NALU type from Annex-B formatted NALU
        private static H264NaluType GetNaluTypeFromAnnexB(byte[] nalu)
        {
            // Skip start code to get to NALU header
            int naluHeaderIndex = -1;

            // Check for 4-byte start code
            if (nalu.Length >= 5 &&
                nalu[0] == 0x00 && nalu[1] == 0x00 && nalu[2] == 0x00 && nalu[3] == 0x01)
            {
                naluHeaderIndex = 4;
            }
            // Check for 3-byte start code
            else if (nalu.Length >= 4 &&
                nalu[0] == 0x00 && nalu[1] == 0x00 && nalu[2] == 0x01)
            {
                naluHeaderIndex = 3;
            }

            if (naluHeaderIndex >= 0 && naluHeaderIndex < nalu.Length)
            {
                return H264NaluParser.GetNaluType(nalu.AsSpan(naluHeaderIndex));
            }

            return H264NaluType.Unspecified;
        }

        private static int GetNaluType(byte[] nalu)
        {
            var naluType = GetNaluTypeFromAnnexB(nalu);
            return (int)naluType;
        }

        public void Dispose()
        {
            _provider?.Dispose();
        }

        private record NaluStatistics
        {
            public int TotalNalus { get; set; }
            public int TotalDataSize { get; set; }
            public int SpsCount { get; set; }
            public int PpsCount { get; set; }
            public int SliceCount { get; set; }
            public int SeiCount { get; set; }
            public int AudCount { get; set; }
            public int OtherCount { get; set; }
            public int MinNaluSize { get; set; }
            public int MaxNaluSize { get; set; }
            public double AverageNaluSize { get; set; }
        }
    }
}
