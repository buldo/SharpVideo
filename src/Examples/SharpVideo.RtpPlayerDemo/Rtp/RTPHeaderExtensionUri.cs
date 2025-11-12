namespace SharpVideo.RtpPlayerDemo.Rtp;

internal class RTPHeaderExtensionUri
{
    public enum Type
    {
        Unknown,
        AbsCaptureTime
    }

    private static Dictionary<string, Type> Types { get; } = new Dictionary<string, Type>() { { "http://www.webrtc.org/experiments/rtp-hdrext/abs-capture-time", Type.AbsCaptureTime } };

    public static Type? GetType(string uri)
    {
        if (!Types.ContainsKey(uri))
        {
            return Type.Unknown;
        }

        return Types[uri];
    }
}
