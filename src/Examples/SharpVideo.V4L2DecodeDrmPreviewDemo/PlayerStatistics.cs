namespace SharpVideo.V4L2DecodeDrmPreviewDemo;

public class PlayerStatistics
{
    private uint _decodedFrames;
    private uint _presentedFrames;

    public TimeSpan DecodeElapsed { get; set; }

    public uint DecodedFrames => _decodedFrames;

    public uint PresentedFrames => _presentedFrames;

    public void IncrementDecodedFrames()
    {
        Interlocked.Increment(ref _decodedFrames);
    }

    public void IncrementPresentedFrames()
    {
        Interlocked.Increment(ref _presentedFrames);
    }
}