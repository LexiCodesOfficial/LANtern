using System.Net.NetworkInformation;
using System.Net.Sockets;
using Lantern.Application.Abstractions;
using Lantern.Application.Utilities;

namespace Lantern.Infrastructure.NetworkScanning;

public sealed class LocalSubnetService : ILocalSubnetService
{
    public IReadOnlyList<string> GetPrivateSubnets()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up
                && networkInterface.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork
                && address.IPv4Mask is not null
                && PrivateIpRangeHelper.IsPrivateIPv4(address.Address))
            .Select(address => $"{address.Address}/{CountPrefixBits(address.IPv4Mask)}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order()
            .ToArray();

    private static int CountPrefixBits(System.Net.IPAddress mask)
        => mask.GetAddressBytes().Sum(value => System.Numerics.BitOperations.PopCount(value));
}
