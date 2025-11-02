using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace SharpVideo.Linux.Native.Media;

/// <summary>
/// Managed representation of <c>struct media_device_info</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SupportedOSPlatform("linux")]
public unsafe struct MediaDeviceInfo
{
    private fixed byte _driver[16];
    private fixed byte _model[32];
    private fixed byte _serial[40];
    private fixed byte _busInfo[32];
    public uint MediaVersion;
    public uint HwRevision;
    public uint DriverVersion;
    private fixed uint _reserved[31];

    public string DriverString
    {
        get
        {
            fixed (byte* ptr = _driver)
            {
                return ReadNullTerminatedString(ptr, 16);
            }
        }
    }

    public string ModelString
    {
        get
        {
            fixed (byte* ptr = _model)
            {
                return ReadNullTerminatedString(ptr, 32);
            }
        }
    }

    public string SerialString
    {
        get
        {
            fixed (byte* ptr = _serial)
            {
                return ReadNullTerminatedString(ptr, 40);
            }
        }
    }

    public string BusInfoString
    {
        get
        {
            fixed (byte* ptr = _busInfo)
            {
                return ReadNullTerminatedString(ptr, 32);
            }
        }
    }

    private static string ReadNullTerminatedString(byte* buffer, int length)
    {
        var span = new ReadOnlySpan<byte>(buffer, length);
        int terminator = span.IndexOf((byte)0);
        if (terminator < 0)
            terminator = length;
        return Encoding.ASCII.GetString(span[..terminator]);
    }
}