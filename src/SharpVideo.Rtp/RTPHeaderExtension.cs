namespace SharpVideo.Rtp;

public class RTPHeaderExtension
{
    public RTPHeaderExtension(int id, string uri)
    {
        Id = id;
        Uri = uri;
    }

    private int Id { get; }
    private string Uri { get; }

    public RTPHeaderExtensionUri.Type? Type => RTPHeaderExtensionUri.GetType(Uri);
}
