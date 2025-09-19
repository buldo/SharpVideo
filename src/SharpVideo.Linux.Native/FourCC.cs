namespace SharpVideo.Linux.Native;

/// <summary>
/// Utility class for working with FOURCC (Four Character Code) values
/// commonly used in V4L2 and DRM for identifying pixel formats.
/// </summary>
public static class FourCC
{
    /// <summary>
    /// Creates a FOURCC value from four characters
    /// </summary>
    /// <param name="a">First character</param>
    /// <param name="b">Second character</param>
    /// <param name="c">Third character</param>
    /// <param name="d">Fourth character</param>
    /// <returns>FOURCC value as uint</returns>
    public static uint FromChars(char a, char b, char c, char d)
    {
        return (uint)((byte)a | ((byte)b << 8) | ((byte)c << 16) | ((byte)d << 24));
    }

    /// <summary>
    /// Converts a FOURCC uint value to a string representation
    /// </summary>
    /// <param name="fourcc">FOURCC value as uint</param>
    /// <returns>String representation of the FOURCC</returns>
    public static string ToString(uint fourcc)
    {
        var chars = new[]
        {
            (char)(fourcc & 0xFF),
            (char)((fourcc >> 8) & 0xFF),
            (char)((fourcc >> 16) & 0xFF),
            (char)((fourcc >> 24) & 0xFF)
        };

        // Replace non-printable characters with '.'
        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsControl(chars[i]) || chars[i] == 0)
            {
                chars[i] = '.';
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// Tries to parse a FOURCC string into a uint value
    /// </summary>
    /// <param name="fourccString">String representation (must be exactly 4 characters)</param>
    /// <param name="fourcc">Output FOURCC value</param>
    /// <returns>True if parsing succeeded, false otherwise</returns>
    public static bool TryParse(string fourccString, out uint fourcc)
    {
        fourcc = 0;

        if (string.IsNullOrEmpty(fourccString) || fourccString.Length != 4)
        {
            return false;
        }

        fourcc = FromChars(fourccString[0], fourccString[1], fourccString[2], fourccString[3]);
        return true;
    }

    /// <summary>
    /// Parses a FOURCC string into a uint value
    /// </summary>
    /// <param name="fourccString">String representation (must be exactly 4 characters)</param>
    /// <returns>FOURCC value as uint</returns>
    /// <exception cref="ArgumentException">Thrown if the string is not exactly 4 characters</exception>
    public static uint Parse(string fourccString)
    {
        if (TryParse(fourccString, out uint fourcc))
        {
            return fourcc;
        }

        throw new ArgumentException("FOURCC string must be exactly 4 characters long", nameof(fourccString));
    }
}