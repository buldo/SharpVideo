# H264NaluProvider Usage Examples

The `H264NaluProvider` now supports two output modes for processing H.264 NAL units:

## 1. WithStartCode Mode (Default)
Returns NALUs in Annex-B format with start codes included.

```csharp
using var provider = new H264NaluProvider(NaluOutputMode.WithStartCode);

// Input H.264 data
var h264Data = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1E }; // SPS NALU

await provider.AppendData(h264Data, CancellationToken.None);
provider.CompleteWriting();

await foreach (var nalu in provider.NaluReader.ReadAllAsync())
{
    // nalu will be: [0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1E]
    // Start code is included
    Console.WriteLine($"NALU with start code: {BitConverter.ToString(nalu)}");
}
```

## 2. WithoutStartCode Mode
Returns only the NALU payload without start codes.

```csharp
using var provider = new H264NaluProvider(NaluOutputMode.WithoutStartCode);

// Input H.264 data
var h264Data = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1E }; // SPS NALU

await provider.AppendData(h264Data, CancellationToken.None);
provider.CompleteWriting();

await foreach (var nalu in provider.NaluReader.ReadAllAsync())
{
    // nalu will be: [0x67, 0x42, 0x00, 0x1E]
    // Start code is NOT included - only payload
    Console.WriteLine($"NALU payload only: {BitConverter.ToString(nalu)}");
}
```

## 3. Processing Multiple NALUs

```csharp
using var provider = new H264NaluProvider(NaluOutputMode.WithoutStartCode);

// Input with multiple NALUs
var spsData = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1E };
var ppsData = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x68, 0xCE, 0x38, 0x80 };
var idrData = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x84, 0x00 };

await provider.AppendData(spsData, CancellationToken.None);
await provider.AppendData(ppsData, CancellationToken.None);
await provider.AppendData(idrData, CancellationToken.None);
provider.CompleteWriting();

await foreach (var nalu in provider.NaluReader.ReadAllAsync())
{
    // Extract NALU type from first byte
    byte naluType = (byte)(nalu[0] & 0x1F);

    switch (naluType)
    {
        case 7:
            Console.WriteLine($"SPS NALU: {BitConverter.ToString(nalu)}");
            break;
        case 8:
            Console.WriteLine($"PPS NALU: {BitConverter.ToString(nalu)}");
            break;
        case 5:
            Console.WriteLine($"IDR Slice NALU: {BitConverter.ToString(nalu)}");
            break;
        default:
            Console.WriteLine($"Other NALU (type {naluType}): {BitConverter.ToString(nalu)}");
            break;
    }
}
```

## 4. Use Cases

### WithStartCode Mode
- **Ideal for**: Writing H.264 files, streaming to decoders that expect Annex-B format
- **Output**: Complete Annex-B formatted NALUs ready for storage or transmission
- **Example use**: Saving parsed NALUs back to an H.264 file

### WithoutStartCode Mode
- **Ideal for**: Analysis, format conversion, hardware decoders expecting raw NALUs
- **Output**: Pure NALU payload data without framing
- **Example use**: Feeding NALUs to hardware decoders that handle their own framing

## Key Features
- **Handles fragmented input**: Can process data in chunks across multiple calls
- **Mixed start codes**: Supports both 3-byte (0x00 0x00 0x01) and 4-byte (0x00 0x00 0x00 0x01) start codes
- **Async processing**: Non-blocking operation with Channel-based output
- **Robust parsing**: Handles malformed or incomplete data gracefully