namespace SharpVideo.Decoding.OhdDemo;

public interface IStreamAccessor
{
    void ProcessIncomingFrame(ReadOnlyMemory<byte> payload);
}