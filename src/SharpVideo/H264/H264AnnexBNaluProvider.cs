using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace SharpVideo.H264;

public class H264AnnexBNaluProvider : IDisposable
{
    private readonly Pipe _pipe = new Pipe();
    private readonly Channel<H264Nalu> _channel = Channel.CreateUnbounded<H264Nalu>(new UnboundedChannelOptions
    { SingleReader = true, SingleWriter = true });

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _processingTask;
    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

    public H264AnnexBNaluProvider()
    {
        _processingTask = ProcessNalusAsync(_cancellationTokenSource.Token);
    }

    public async ValueTask AppendData(byte[] data, CancellationToken cancellationToken)
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
        byte[] buffer = _arrayPool.Rent(128 * 1024); // 128KB initial buffer
        int bufferLength = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(cancellationToken);
                var sequence = result.Buffer;

                if (sequence.IsEmpty && result.IsCompleted)
                {
                    // Process any remaining data in buffer as the last NALU
                    if (bufferLength > 0)
                    {
                        ProcessFinalNaluSync(buffer.AsSpan(0, bufferLength));
                    }
                    break;
                }

                foreach (var segment in sequence)
                {
                    bufferLength = ProcessBytesSync(segment.Span, ref buffer, bufferLength);
                }

                reader.AdvanceTo(sequence.End);

                if (result.IsCompleted)
                {
                    // Process any remaining data in buffer as the last NALU
                    if (bufferLength > 0)
                    {
                        ProcessFinalNaluSync(buffer.AsSpan(0, bufferLength));
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
            _arrayPool.Return(buffer);
            _channel.Writer.TryComplete();
        }
    }

    private int ProcessBytesSync(ReadOnlySpan<byte> data, ref byte[] buffer, int bufferLength)
    {
        // Ensure buffer has enough capacity
        int requiredLength = bufferLength + data.Length;
        if (requiredLength > buffer.Length)
        {
            // Need to resize - rent bigger buffer
            byte[] newBuffer = _arrayPool.Rent(requiredLength * 2);
            buffer.AsSpan(0, bufferLength).CopyTo(newBuffer);
            _arrayPool.Return(buffer);
            buffer = newBuffer;
        }

        // Append data directly without ToArray
        data.CopyTo(buffer.AsSpan(bufferLength));
        bufferLength += data.Length;

        // Look for start codes and extract NALUs in Annex-B format
        return ExtractNalusFromBufferSync(buffer, bufferLength);
    }

    private int ExtractNalusFromBufferSync(byte[] buffer, int bufferLength)
    {
        if (bufferLength < 4)
            return bufferLength;

        // Use stackalloc for small arrays, List for larger
        Span<int> startPositionsStack = stackalloc int[64];
        List<int>? startPositionsList = null;
        int startPositionsCount = 0;

        // Find all start code positions - optimized loop
        var bufferSpan = buffer.AsSpan(0, bufferLength);
        for (int i = 0; i <= bufferLength - 3; i++)
        {
            // Check for 4-byte start code: 0x00 0x00 0x00 0x01
            if (i <= bufferLength - 4 &&
                bufferSpan[i] == 0x00 && bufferSpan[i + 1] == 0x00 &&
                bufferSpan[i + 2] == 0x00 && bufferSpan[i + 3] == 0x01)
            {
                if (startPositionsCount < 64)
                    startPositionsStack[startPositionsCount] = i;
                else
                {
                    if (startPositionsList == null)
                    {
                        startPositionsList = new List<int>(128);
                        for (int j = 0; j < 64; j++)
                            startPositionsList.Add(startPositionsStack[j]);
                    }
                    startPositionsList.Add(i);
                }
                startPositionsCount++;
                i += 3; // Skip ahead to avoid overlapping matches
            }
            // Check for 3-byte start code: 0x00 0x00 0x01
            else if (bufferSpan[i] == 0x00 && bufferSpan[i + 1] == 0x00 && bufferSpan[i + 2] == 0x01)
            {
                if (startPositionsCount < 64)
                    startPositionsStack[startPositionsCount] = i;
                else
                {
                    if (startPositionsList == null)
                    {
                        startPositionsList = new List<int>(128);
                        for (int j = 0; j < 64; j++)
                            startPositionsList.Add(startPositionsStack[j]);
                    }
                    startPositionsList.Add(i);
                }
                startPositionsCount++;
                i += 2; // Skip ahead to avoid overlapping matches
            }
        }

        if (startPositionsCount == 0)
            return bufferLength;

        // Extract complete NALUs (always include start codes)
        for (int i = 0; i < startPositionsCount - 1; i++)
        {
            var startPos = startPositionsList != null ? startPositionsList[i] : startPositionsStack[i];
            var nextStartPos = startPositionsList != null ? startPositionsList[i + 1] : startPositionsStack[i + 1];

            var startCodeLength = GetStartCodeLength(bufferSpan, startPos);
            var naluLength = nextStartPos - startPos;

            if (naluLength > 0)
            {
                // Use ArrayPool for NALU data
                var naluData = _arrayPool.Rent(naluLength);
                bufferSpan.Slice(startPos, naluLength).CopyTo(naluData);

                var nalu = new H264Nalu(naluData.AsSpan(0, naluLength).ToArray(), startCodeLength);
                _arrayPool.Return(naluData);

                // Use synchronous write - we're already in a background task
                if (!_channel.Writer.TryWrite(nalu))
                {
                    // If channel is full, we need to wait, but this should be rare
                    _channel.Writer.WriteAsync(nalu).AsTask().GetAwaiter().GetResult();
                }
            }
        }

        // Remove processed data from buffer efficiently
        var lastStartPos = startPositionsList != null
            ? startPositionsList[startPositionsCount - 1]
            : startPositionsStack[startPositionsCount - 1];

        if (lastStartPos > 0)
        {
            int remainingCount = bufferLength - lastStartPos;
            // Move remaining bytes to the beginning
            bufferSpan.Slice(lastStartPos, remainingCount).CopyTo(bufferSpan);
            return remainingCount;
        }

        return bufferLength;
    }

    private void ProcessFinalNaluSync(Span<byte> buffer)
    {
        if (buffer.Length == 0)
            return;

        // Check if the buffer starts with a start code and has data after it
        bool startsWithStartCode = false;
        int startCodeLength = 0;

        if (buffer.Length >= 4 &&
            buffer[0] == 0x00 && buffer[1] == 0x00 &&
            buffer[2] == 0x00 && buffer[3] == 0x01)
        {
            startsWithStartCode = true;
            startCodeLength = 4;
        }
        else if (buffer.Length >= 3 &&
            buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0x01)
        {
            startsWithStartCode = true;
            startCodeLength = 3;
        }

        if (startsWithStartCode)
        {
            // Only process if there's actual NALU data after the start code
            if (buffer.Length > startCodeLength)
            {
                // Include start code (Annex-B format)
                var finalNalu = buffer.ToArray();
                var nalu = new H264Nalu(finalNalu, startCodeLength);
                _channel.Writer.TryWrite(nalu);
            }
            // If buffer contains only a start code with no data, ignore it
        }
        else
        {
            // No start codes found - add a start code to make it Annex-B format
            var finalNalu = new byte[4 + buffer.Length];
            finalNalu[0] = 0x00;
            finalNalu[1] = 0x00;
            finalNalu[2] = 0x00;
            finalNalu[3] = 0x01;
            buffer.CopyTo(finalNalu.AsSpan(4));

            var nalu = new H264Nalu(finalNalu, 4); // Payload starts after the added start code
            _channel.Writer.TryWrite(nalu);
        }
    }

    private static int GetStartCodeLength(ReadOnlySpan<byte> buffer, int position)
    {
        if (position + 3 < buffer.Length &&
            buffer[position] == 0x00 && buffer[position + 1] == 0x00 &&
            buffer[position + 2] == 0x00 && buffer[position + 3] == 0x01)
        {
            return 4;
        }
        if (position + 2 < buffer.Length &&
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