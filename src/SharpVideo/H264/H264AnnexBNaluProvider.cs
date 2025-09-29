using System.IO.Pipelines;
using System.Threading.Channels;

namespace SharpVideo.H264;

public class H264Nalu
{
    private readonly byte[] _data;
    private readonly int _payloadStart;

    public H264Nalu(byte[] data, int payloadStart)
    {
        _data = data;
        _payloadStart = payloadStart;
    }

    public ReadOnlySpan<byte> Data => _data;
    public ReadOnlySpan<byte> WithoutHeader => _data.AsSpan(_payloadStart);
}

public class H264AnnexBNaluProvider : IDisposable
{
    private readonly Pipe _pipe = new Pipe();
    private readonly Channel<H264Nalu> _channel = Channel.CreateUnbounded<H264Nalu>(new UnboundedChannelOptions
    { SingleReader = true, SingleWriter = true });

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _processingTask;
    private readonly NaluMode _outputMode;

    public H264AnnexBNaluProvider(NaluMode outputMode = NaluMode.WithStartCode)
    {
        _outputMode = outputMode;
        _processingTask = ProcessNalusAsync(_cancellationTokenSource.Token);
    }

    public async Task AppendData(byte[] data, CancellationToken cancellationToken)
    {
        await _pipe.Writer.WriteAsync(data, cancellationToken);
    }

    public ChannelReader<H264Nalu> NaluReader => _channel.Reader;

    public void CompleteWriting()
    {
        _pipe.Writer.Complete();
    }

    private async Task ProcessNalusAsync(CancellationToken cancellationToken)
    {
        var reader = _pipe.Reader;
        var buffer = new List<byte>();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(cancellationToken);
                var sequence = result.Buffer;

                if (sequence.IsEmpty && result.IsCompleted)
                {
                    // Process any remaining data in buffer as the last NALU
                    if (buffer.Count > 0)
                    {
                        await ProcessFinalNalu(buffer, cancellationToken);
                    }
                    break;
                }

                foreach (var segment in sequence)
                {
                    var segmentArray = segment.Span.ToArray();
                    await ProcessBytesAsync(segmentArray, buffer, cancellationToken);
                }

                reader.AdvanceTo(sequence.End);

                if (result.IsCompleted)
                {
                    // Process any remaining data in buffer as the last NALU
                    if (buffer.Count > 0)
                    {
                        await ProcessFinalNalu(buffer, cancellationToken);
                    }
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _channel.Writer.TryComplete(ex);
            return;
        }
        finally
        {
            _channel.Writer.TryComplete();
        }
    }

    private async Task ProcessBytesAsync(byte[] data, List<byte> buffer, CancellationToken cancellationToken)
    {
        buffer.AddRange(data);

        // Look for start codes and extract NALUs in Annex-B format
        await ExtractNalusFromBuffer(buffer, cancellationToken);
    }

    private async Task ExtractNalusFromBuffer(List<byte> buffer, CancellationToken cancellationToken)
    {
        var startPositions = new List<int>();

        // Find all start code positions
        for (int i = 0; i <= buffer.Count - 3; i++)
        {
            // Check for 4-byte start code: 0x00 0x00 0x00 0x01
            if (i <= buffer.Count - 4 &&
                buffer[i] == 0x00 && buffer[i + 1] == 0x00 &&
                buffer[i + 2] == 0x00 && buffer[i + 3] == 0x01)
            {
                startPositions.Add(i);
                i += 3; // Skip ahead to avoid overlapping matches
            }
            // Check for 3-byte start code: 0x00 0x00 0x01
            else if (buffer[i] == 0x00 && buffer[i + 1] == 0x00 && buffer[i + 2] == 0x01)
            {
                startPositions.Add(i);
                i += 2; // Skip ahead to avoid overlapping matches
            }
        }

        // Extract complete NALUs based on output mode
        for (int i = 0; i < startPositions.Count - 1; i++)
        {
            var startPos = startPositions[i];
            var nextStartPos = startPositions[i + 1];

            byte[] naluData;

            if (_outputMode == NaluMode.WithStartCode)
            {
                // Include the start code in the NALU (Annex-B format)
                var naluLength = nextStartPos - startPos;

                if (naluLength > 0)
                {
                    naluData = new byte[naluLength];
                    for (int j = 0; j < naluLength; j++)
                    {
                        naluData[j] = buffer[startPos + j];
                    }

                    await _channel.Writer.WriteAsync(naluData, cancellationToken);
                }
            }
            else // WithoutStartCode
            {
                // Skip the start code, only include NALU payload
                var startCodeLength = GetStartCodeLength(buffer, startPos);
                var naluDataStart = startPos + startCodeLength;
                var naluDataLength = nextStartPos - naluDataStart;

                if (naluDataLength > 0)
                {
                    naluData = new byte[naluDataLength];
                    for (int j = 0; j < naluDataLength; j++)
                    {
                        naluData[j] = buffer[naluDataStart + j];
                    }

                    await _channel.Writer.WriteAsync(naluData, cancellationToken);
                }
            }
        }

        // Remove processed data from buffer, keeping only the last start code and any data after it
        if (startPositions.Count > 0)
        {
            var lastStartPos = startPositions[startPositions.Count - 1];
            var remainingData = new List<byte>();

            for (int i = lastStartPos; i < buffer.Count; i++)
            {
                remainingData.Add(buffer[i]);
            }

            buffer.Clear();
            buffer.AddRange(remainingData);
        }
    }

    private async Task ProcessFinalNalu(List<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Count == 0)
            return;

        // Check if the buffer starts with a start code and has data after it
        bool startsWithStartCode = false;
        int startCodeLength = 0;

        if (buffer.Count >= 4 &&
            buffer[0] == 0x00 && buffer[1] == 0x00 &&
            buffer[2] == 0x00 && buffer[3] == 0x01)
        {
            startsWithStartCode = true;
            startCodeLength = 4;
        }
        else if (buffer.Count >= 3 &&
            buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0x01)
        {
            startsWithStartCode = true;
            startCodeLength = 3;
        }

        if (startsWithStartCode)
        {
            // Only process if there's actual NALU data after the start code
            if (buffer.Count > startCodeLength)
            {
                byte[] finalNalu;

                if (_outputMode == NaluMode.WithStartCode)
                {
                    // Include start code
                    finalNalu = buffer.ToArray();
                }
                else // WithoutStartCode
                {
                    // Skip start code, only include NALU payload
                    finalNalu = new byte[buffer.Count - startCodeLength];
                    for (int i = 0; i < finalNalu.Length; i++)
                    {
                        finalNalu[i] = buffer[startCodeLength + i];
                    }
                }

                await _channel.Writer.WriteAsync(finalNalu, cancellationToken);
            }
            // If buffer contains only a start code with no data, ignore it
        }
        else
        {
            // No start codes found
            if (_outputMode == NaluMode.WithStartCode)
            {
                // Add a start code to make it Annex-B format
                var annexBNalu = new byte[4 + buffer.Count];
                annexBNalu[0] = 0x00;
                annexBNalu[1] = 0x00;
                annexBNalu[2] = 0x00;
                annexBNalu[3] = 0x01;
                buffer.CopyTo(annexBNalu, 4);

                await _channel.Writer.WriteAsync(annexBNalu, cancellationToken);
            }
            else // WithoutStartCode
            {
                // Just use the raw data as-is
                await _channel.Writer.WriteAsync(buffer.ToArray(), cancellationToken);
            }
        }
    }

    private static int GetStartCodeLength(List<byte> buffer, int position)
    {
        if (position + 3 < buffer.Count &&
            buffer[position] == 0x00 && buffer[position + 1] == 0x00 &&
            buffer[position + 2] == 0x00 && buffer[position + 3] == 0x01)
        {
            return 4;
        }
        if (position + 2 < buffer.Count &&
            buffer[position] == 0x00 && buffer[position + 1] == 0x00 &&
            buffer[position + 2] == 0x01)
        {
            return 3;
        }
        return 0;
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _pipe.Writer.Complete();

        try
        {
            _processingTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Ignore cancellation exceptions during disposal
        }

        _cancellationTokenSource.Dispose();
        _channel.Writer.TryComplete();
    }
}