namespace SharpVideo.H264;

/// <summary>
/// Generic Parsing Options
/// </summary>
public class ParsingOptions
{
    public ParsingOptions()
    {
        add_offset = (true);
        add_length = (true);
        add_parsed_length = (true);
        add_checksum = (true);
        add_resolution = (true);
    }

    public bool add_offset;
    public bool add_length;
    public bool add_parsed_length;
    public bool add_checksum;
    public bool add_resolution;
}