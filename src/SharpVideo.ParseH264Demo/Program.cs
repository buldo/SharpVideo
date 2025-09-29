using SharpVideo.H264;

namespace SharpVideo.ParseH264Demo;

internal class Program
{
    static async Task Main(string[] args)
    {
        var file = await File.ReadAllBytesAsync("test_video.h264");
        var provider = new H264AnnexBNaluProvider(NaluMode.WithoutStartCode);
        await provider.AppendData(file, CancellationToken.None);

        var streamState = new H264BitstreamParserState();
        var parsingOptions = new ParsingOptions();
        await foreach (var nalu in provider.NaluReader.ReadAllAsync())
        {
            var naluState = H264NalUnitParser.ParseNalUnit(nalu, streamState, parsingOptions);
            var naluType = (NalUnitType)naluState.nal_unit_header.nal_unit_type;
            Console.Write($"Nalu Type: {naluType}, Size: {nalu.Length} bytes");
            var naluData = nalu.AsSpan(1);
            if (naluType == NalUnitType.PPS_NUT)
            {
                var pps = naluState.nal_unit_payload.pps;
                Console.WriteLine($", pic_parameter_set_id: {pps.pic_parameter_set_id}");
            }
            else if (naluType == NalUnitType.SPS_NUT)
            {
                var spsState = naluState.nal_unit_payload.sps;
                Console.WriteLine($", profile_type: {spsState.sps_data.profile_type}");
            }
            else
            {
                var aaa = naluState.nal_unit_payload.slice_layer_without_partitioning_rbsp;
                Console.WriteLine($"Frame num: {aaa.slice_header.frame_num}");
            }
        }
    }
}