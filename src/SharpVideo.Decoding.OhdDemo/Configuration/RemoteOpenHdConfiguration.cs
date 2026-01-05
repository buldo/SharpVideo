namespace SharpVideo.Decoding.OhdDemo.Configuration;

public class RemoteOpenHdConfiguration
{
    public const string Key = "RemoteOpenHd";

    public bool IsEnabled { get; set; }

    public string Ip { get; set; }

    public int Port { get; set; }
}