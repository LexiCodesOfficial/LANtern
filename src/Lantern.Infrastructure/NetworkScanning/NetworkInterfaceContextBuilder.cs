using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Lantern.Application;
using Lantern.Application.Utilities;

namespace Lantern.Infrastructure.NetworkScanning;

public static class NetworkInterfaceContextBuilder
{
    public static NetworkScanContext Build(NetworkScanOptions options)
    {
        var addresses = new List<IPAddress>();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                if (!options.AllowPublicRanges && !PrivateIpRangeHelper.IsPrivateIPv4(unicast.Address))
                {
                    continue;
                }

                addresses.AddRange(SubnetHelper.ExpandSubnet(unicast.Address, unicast.IPv4Mask, options.MaxHostsPerInterface));
            }
        }

        return new NetworkScanContext(
            options,
            addresses.DistinctBy(address => address.ToString()).OrderBy(SubnetHelper.ToUInt32).ToArray(),
            DateTimeOffset.UtcNow);
    }
}
