using SharpVideo.H264;

namespace SharpVideo.ParseH264Demo;

internal class Program
{
    static async Task Main(string[] args)
    {
        var file = await File.ReadAllBytesAsync("test_video.h264");
        var provider = new H264AnnexBNaluProvider(NaluMode.WithoutStartCode);
        await provider.AppendData(file, CancellationToken.None);

        var naluParser = new H264NaluParser(NaluMode.WithoutStartCode);
        await foreach (var nalu in provider.NaluReader.ReadAllAsync())
        {
            var naluType = naluParser.GetNaluType(nalu);
            Console.Write($"Nalu Type: {naluType}, Size: {nalu.Length} bytes");
            if(naluType == H264NaluType.PictureParameterSet)
            {
                var ppsState = H264PpsParser.ParsePps(nalu.AsSpan(1),1);
                Console.WriteLine($", pic_parameter_set_id: {ppsState.pic_parameter_set_id}");
            }
            else
            {
                Console.WriteLine();
            }
        }
    }
}