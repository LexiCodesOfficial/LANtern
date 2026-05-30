using System.Globalization;
using System.Net;

namespace Lantern.Application.Utilities;

public static class SubnetHelper
{
    public static IEnumerable<IPAddress> ExpandSubnet(IPAddress address, IPAddress? mask, int maxHosts)
    {
        var ip = ToUInt32(address);
        var maskValue = mask is null ? 0xFFFFFF00u : ToUInt32(mask);
        var network = ip & maskValue;
        var broadcast = network | ~maskValue;
        var total = broadcast - network - 1;

        if (total > maxHosts)
        {
            maskValue = 0xFFFFFF00u;
            network = ip & maskValue;
            broadcast = network | ~maskValue;
        }

        for (var value = network + 1; value < broadcast; value++)
        {
            if (value != ip)
            {
                yield return FromUInt32(value);
            }
        }
    }

    public static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return uint.Parse(string.Concat(bytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture))), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static IPAddress FromUInt32(uint value)
        => new([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
}
