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
            _provider = new H264NaluProvider(NaluOutputMode.WithStartCode);
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
            _provider = new H264NaluProvider(NaluOutputMode.WithoutStartCode);
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
            _provider = new H264NaluProvider(NaluOutputMode.WithStartCode);
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
            _provider = new H264NaluProvider(NaluOutputMode.WithoutStartCode);
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
            _provider = new H264NaluProvider(NaluOutputMode.WithStartCode);
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
            _provider = new H264NaluProvider(NaluOutputMode.WithoutStartCode);
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
            _provider = new H264NaluProvider(NaluOutputMode.WithStartCode);
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
            _provider = new H264NaluProvider(NaluOutputMode.WithStartCode);
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
            _provider = new H264NaluProvider(NaluOutputMode.WithStartCode);

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
            _provider = new H264NaluProvider(NaluOutputMode.WithStartCode);
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
            _provider = new H264NaluProvider(NaluOutputMode.WithoutStartCode);
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
            _provider = new H264NaluProvider(NaluOutputMode.WithStartCode);
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

            // Verify each NALU starts with a start code (Annex-B format)
            foreach (var nalu in nalus)
            {
                Assert.True(nalu.Length >= 4, "NALU should have at least 4 bytes (start code + data)");

                // Check for 4-byte start code or 3-byte start code
                bool has4ByteStartCode = nalu.Length >= 4 &&
                    nalu[0] == 0x00 && nalu[1] == 0x00 && nalu[2] == 0x00 && nalu[3] == 0x01;
                bool has3ByteStartCode = nalu.Length >= 3 &&
                    nalu[0] == 0x00 && nalu[1] == 0x00 && nalu[2] == 0x01;

                Assert.True(has4ByteStartCode || has3ByteStartCode, "NALU should start with Annex-B start code");
            }

            // Verify we have some expected NALU types (checking after start code)
            var spsFound = nalus.Any(nalu =>
            {
                var naluType = GetNaluType(nalu);
                return naluType == 7; // SPS
            });

            var ppsFound = nalus.Any(nalu =>
            {
                var naluType = GetNaluType(nalu);
                return naluType == 8; // PPS
            });

            var sliceFound = nalus.Any(nalu =>
            {
                var naluType = GetNaluType(nalu);
                return naluType == 1; // Non-IDR slice
            });

            Assert.True(spsFound, "Should find SPS NALU");
            Assert.True(ppsFound, "Should find PPS NALU");
            Assert.True(sliceFound, "Should find slice NALUs");
        }

        [Fact]
        public async Task Should_Handle_Concurrent_Appends()
        {
            // Arrange
            _provider = new H264NaluProvider(NaluOutputMode.WithStartCode);
            var nalu1 = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1E };
            var nalu2 = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x68, 0xCE, 0x38, 0x80 };
            var nalu3 = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x84, 0x00 };
            var endMarker = new byte[] { 0x00, 0x00, 0x00, 0x01 };

            var nalus = new List<byte[]>();

            // Act
            var readTask = Task.Run(async () =>
            {
                await foreach (var nalu in _provider.NaluReader.ReadAllAsync())
                {
                    nalus.Add(nalu);
                }
            });

            var appendTasks = new[]
            {
                _provider.AppendData(nalu1, CancellationToken.None),
                _provider.AppendData(nalu2, CancellationToken.None),
                _provider.AppendData(nalu3, CancellationToken.None),
                _provider.AppendData(endMarker, CancellationToken.None)
            };

            await Task.WhenAll(appendTasks);
            _provider.CompleteWriting();
            await readTask;

            // Assert
            Assert.Equal(3, nalus.Count);
            Assert.Equal(nalu1, nalus[0]); // Should include start code
            Assert.Equal(nalu2, nalus[1]); // Should include start code
            Assert.Equal(nalu3, nalus[2]); // Should include start code
        }

        [Fact]
        public async Task Should_Handle_Cancellation()
        {
            // Arrange
            _provider = new H264NaluProvider(NaluOutputMode.WithStartCode);
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMilliseconds(100));

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                var largeData = new byte[1024 * 1024]; // 1MB of data
                await _provider.AppendData(largeData, cts.Token);
            });
        }

        [Fact]
        public void Should_Complete_Channel_When_Disposed()
        {
            // Arrange
            var provider = new H264NaluProvider();

            // Act
            provider.Dispose();

            // Assert
            Assert.True(provider.NaluReader.Completion.IsCompleted);
        }

        private static int GetNaluType(byte[] nalu)
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
                return nalu[naluHeaderIndex] & 0x1F; // Extract NALU type from header
            }

            return -1; // Invalid
        }

        public void Dispose()
        {
            _provider?.Dispose();
        }
    }
}
