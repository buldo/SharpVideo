namespace SharpVideo.V4L2StatelessDecoder.Services;

internal class DpbEntry
{
    public uint FrameNum { get; set; }
    public uint PicOrderCnt { get; set; }
    public bool IsReference { get; set; }
    public bool IsLongTerm { get; set; }
}