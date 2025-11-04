using System.Net;
using System.Net.Sockets;

namespace SharpVideo.RtpPlayerDemo.Rtp;

internal static class TypeExtensions
{
    public static bool IsPrivate(this IPAddress address)
    {
        if (IPAddress.TryParse(address.ToString(), out var ipAddress))
        {
            if (IPAddress.IsLoopback(ipAddress) || ipAddress.IsIPv6LinkLocal || ipAddress.IsIPv6SiteLocal)
            {
                return true;
            }
            else if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] addrBytes = ipAddress.GetAddressBytes();
                if ((addrBytes[0] == 10) ||
                    (addrBytes[0] == 172 && (addrBytes[1] >= 16 && addrBytes[1] <= 31)) ||
                    (addrBytes[0] == 192 && addrBytes[1] == 168))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
