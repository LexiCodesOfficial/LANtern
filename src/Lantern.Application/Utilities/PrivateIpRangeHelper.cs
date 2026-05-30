using System.Net;

namespace Lantern.Application.Utilities;

public static class PrivateIpRangeHelper
{
    public static bool IsPrivateIPv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
            || bytes[0] == 192 && bytes[1] == 168);
    }
}
