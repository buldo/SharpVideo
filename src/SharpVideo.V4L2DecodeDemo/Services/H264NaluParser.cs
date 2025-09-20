using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace SharpVideo.V4L2DecodeDemo.Services;

/// <summary>
/// Represents a single H.264 NALU (Network Abstraction Layer Unit)
/// </summary>
public class H264Nalu
{
    /// <summary>
    /// The NALU type (5 bits from the NALU header)
    /// </summary>
    public byte Type { get; init; }

    /// <summary>
    /// The raw NALU data including the start code
    /// </summary>
    public required byte[] Data { get; init; }

    /// <summary>
    /// The position of this NALU in the original stream
    /// </summary>
    public long Position { get; init; }

    /// <summary>
    /// Whether this NALU is a keyframe (contains SPS, PPS, or IDR slice)
    /// </summary>
    public bool IsKeyFrame { get; init; }

    /// <summary>
    /// Human-readable description of the NALU type
    /// </summary>
    public string TypeDescription => GetNaluTypeDescription(Type);

    /// <summary>
    /// Gets a description for a NALU type
    /// </summary>
    private static string GetNaluTypeDescription(byte naluType)
    {
        return naluType switch
        {
            1 => "Non-IDR Slice",
            2 => "Data Partition A",
            3 => "Data Partition B",
            4 => "Data Partition C",
            5 => "IDR Slice",
            6 => "SEI (Supplemental Enhancement Information)",
            7 => "SPS (Sequence Parameter Set)",
            8 => "PPS (Picture Parameter Set)",
            9 => "Access Unit Delimiter",
            10 => "End of Sequence",
            11 => "End of Stream",
            12 => "Filler Data",
            13 => "SPS Extension",
            14 => "Prefix NAL Unit",
            15 => "Subset SPS",
            19 => "Auxiliary Coded Picture",
            20 => "Coded Slice Extension",
            _ => $"Unknown ({naluType})"
        };
    }
}

/// <summary>
/// Utility class for parsing H.264 streams and extracting individual NALUs
/// </summary>
public class H264NaluParser
{
    private readonly ILogger<H264NaluParser> _logger;

    public H264NaluParser(ILogger<H264NaluParser> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Parses H.264 data and extracts individual NALUs
    /// </summary>
    /// <param name="data">The H.264 stream data</param>
    /// <param name="offset">Starting offset in the data</param>
    /// <param name="length">Length of data to parse</param>
    /// <param name="streamPosition">Current position in the stream (for logging)</param>
    /// <returns>List of parsed NALUs</returns>
    public List<H264Nalu> ParseNalus(byte[] data, int offset = 0, int? length = null, long streamPosition = 0)
    {
        var nalus = new List<H264Nalu>();

        if (data == null || data.Length == 0)
        {
            _logger.LogWarning("Empty or null data provided to NALU parser");
            return nalus;
        }

        int actualLength = length ?? (data.Length - offset);
        int endPosition = offset + actualLength;

        if (offset < 0 || offset >= data.Length || endPosition > data.Length)
        {
            _logger.LogError("Invalid offset ({Offset}) or length ({Length}) for data of size {Size}",
                offset, actualLength, data.Length);
            return nalus;
        }

        _logger.LogDebug("Parsing NALUs from {Length} bytes at stream position {Position}",
            actualLength, streamPosition);

        int position = offset;

        while (position < endPosition - 3) // Need at least 4 bytes for start code + NALU header
        {
            // Find start code
            var startCodeInfo = FindStartCode(data, position, endPosition);
            if (!startCodeInfo.HasValue)
            {
                // No more start codes found
                break;
            }

            int startCodeStart = startCodeInfo.Value.Position;
            int startCodeLength = startCodeInfo.Value.Length;
            int naluHeaderPos = startCodeStart + startCodeLength;

            // Ensure we have at least one byte for NALU header
            if (naluHeaderPos >= endPosition)
            {
                _logger.LogWarning("Start code found at end of data with no NALU header");
                break;
            }

            // Extract NALU type from header
            byte naluHeader = data[naluHeaderPos];
            byte naluType = (byte)(naluHeader & 0x1F);

            // Find the end of this NALU (next start code or end of data)
            int naluEnd = endPosition;
            for (int searchPos = naluHeaderPos + 1; searchPos < endPosition - 3; searchPos++)
            {
                var nextStartCode = FindStartCode(data, searchPos, endPosition);
                if (nextStartCode.HasValue)
                {
                    naluEnd = nextStartCode.Value.Position;
                    break;
                }
            }

            // Extract NALU data including start code
            int naluDataLength = naluEnd - startCodeStart;
            if (naluDataLength <= startCodeLength)
            {
                _logger.LogWarning("NALU too short: {Length} bytes", naluDataLength);
                position = naluHeaderPos + 1;
                continue;
            }

            byte[] naluData = new byte[naluDataLength];
            Array.Copy(data, startCodeStart, naluData, 0, naluDataLength);

            // Determine if this is a keyframe NALU
            bool isKeyFrame = IsKeyFrameNalu(naluType);

            var nalu = new H264Nalu
            {
                Type = naluType,
                Data = naluData,
                Position = streamPosition + (startCodeStart - offset),
                IsKeyFrame = isKeyFrame
            };

            nalus.Add(nalu);

            _logger.LogTrace("Found NALU: Type={Type} ({Description}), Size={Size} bytes, KeyFrame={IsKeyFrame}",
                naluType, nalu.TypeDescription, naluDataLength, isKeyFrame);

            // Move to next NALU
            position = naluEnd;
        }

        _logger.LogDebug("Parsed {Count} NALUs from {Length} bytes", nalus.Count, actualLength);

        return nalus;
    }

    /// <summary>
    /// Parses NALUs from a stream one at a time
    /// </summary>
    /// <param name="stream">Input stream</param>
    /// <param name="bufferSize">Size of the read buffer</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of NALUs</returns>
    public async IAsyncEnumerable<H264Nalu> ParseNalusFromStreamAsync(
        Stream stream,
        int bufferSize = 64 * 1024,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new byte[bufferSize];
        var overflow = new List<byte>(); // For incomplete NALUs at buffer boundaries
        long streamPosition = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            // Read next chunk
            int bytesRead = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (bytesRead == 0)
            {
                // End of stream - process any remaining data in overflow
                if (overflow.Count > 0)
                {
                    var finalNalus = ParseNalus(overflow.ToArray(), 0, null, streamPosition - overflow.Count);
                    foreach (var nalu in finalNalus)
                    {
                        yield return nalu;
                    }
                }
                break;
            }

            // Combine overflow from previous read with new data
            byte[] combinedData;
            int dataOffset = 0;

            if (overflow.Count > 0)
            {
                combinedData = new byte[overflow.Count + bytesRead];
                overflow.CopyTo(combinedData);
                Array.Copy(buffer, 0, combinedData, overflow.Count, bytesRead);
                streamPosition -= overflow.Count; // Adjust for reprocessed data
            }
            else
            {
                combinedData = buffer;
                dataOffset = 0;
            }

            int actualDataLength = overflow.Count + bytesRead;

            // Parse NALUs from combined data
            var nalus = ParseNalus(combinedData, dataOffset, actualDataLength, streamPosition);

            // Find where the last complete NALU ends
            int lastCompleteNaluEnd = 0;
            foreach (var nalu in nalus)
            {
                int naluRelativeEnd = (int)(nalu.Position - streamPosition) + nalu.Data.Length;
                if (naluRelativeEnd > lastCompleteNaluEnd)
                {
                    lastCompleteNaluEnd = naluRelativeEnd;
                }

                yield return nalu;
            }

            // Save incomplete data for next iteration
            overflow.Clear();
            if (lastCompleteNaluEnd < actualDataLength)
            {
                int remainingBytes = actualDataLength - lastCompleteNaluEnd;
                for (int i = lastCompleteNaluEnd; i < actualDataLength; i++)
                {
                    overflow.Add(combinedData[i]);
                }
                _logger.LogTrace("Saved {Bytes} bytes for next iteration", remainingBytes);
            }

            streamPosition += bytesRead;
        }
    }

    /// <summary>
    /// Finds the next H.264 start code in the data
    /// </summary>
    /// <param name="data">Data to search</param>
    /// <param name="startPos">Starting position</param>
    /// <param name="endPos">End position (exclusive)</param>
    /// <returns>Start code position and length, or null if not found</returns>
    private (int Position, int Length)? FindStartCode(byte[] data, int startPos, int endPos)
    {
        for (int i = startPos; i <= endPos - 3; i++)
        {
            // Look for 0x000001 (3-byte start code)
            if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1)
            {
                return (i, 3);
            }

            // Look for 0x00000001 (4-byte start code)
            if (i <= endPos - 4 && data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 0 && data[i + 3] == 1)
            {
                return (i, 4);
            }
        }

        return null;
    }

    /// <summary>
    /// Determines if a NALU type represents a keyframe
    /// </summary>
    /// <param name="naluType">The NALU type</param>
    /// <returns>True if this NALU type is part of a keyframe</returns>
    private static bool IsKeyFrameNalu(byte naluType)
    {
        return naluType switch
        {
            5 => true,  // IDR slice
            7 => true,  // SPS
            8 => true,  // PPS
            _ => false
        };
    }

    /// <summary>
    /// Validates that a NALU has proper structure
    /// </summary>
    /// <param name="nalu">The NALU to validate</param>
    /// <returns>True if the NALU appears valid</returns>
    public bool ValidateNalu(H264Nalu nalu)
    {
        if (nalu?.Data == null || nalu.Data.Length < 4)
        {
            _logger.LogWarning("NALU validation failed: null or too short");
            return false;
        }

        // Check for valid start code
        bool hasValidStartCode =
            (nalu.Data[0] == 0 && nalu.Data[1] == 0 && nalu.Data[2] == 1) ||
            (nalu.Data.Length >= 4 && nalu.Data[0] == 0 && nalu.Data[1] == 0 && nalu.Data[2] == 0 && nalu.Data[3] == 1);

        if (!hasValidStartCode)
        {
            _logger.LogWarning("NALU validation failed: invalid start code");
            return false;
        }

        // Check NALU header
        int headerPos = nalu.Data[2] == 1 ? 3 : 4;
        if (headerPos >= nalu.Data.Length)
        {
            _logger.LogWarning("NALU validation failed: no header after start code");
            return false;
        }

        byte naluHeader = nalu.Data[headerPos];
        byte extractedType = (byte)(naluHeader & 0x1F);

        if (extractedType != nalu.Type)
        {
            _logger.LogWarning("NALU validation failed: type mismatch. Expected {Expected}, got {Actual}",
                nalu.Type, extractedType);
            return false;
        }

        // Check forbidden_zero_bit (bit 7 of NALU header should be 0)
        if ((naluHeader & 0x80) != 0)
        {
            _logger.LogWarning("NALU validation failed: forbidden_zero_bit is set");
            return false;
        }

        return true;
    }
}