using System.Net;
using System.Runtime.InteropServices;
using Lantern.Application;
using Lantern.Application.Abstractions;
using Lantern.Application.Utilities;
using Lantern.Infrastructure.NetworkScanning.Discovery;

namespace Lantern.Infrastructure.NetworkScanning.Enrichment;

public sealed class MacAddressEnrichmentProvider : IEnrichmentProvider
{
    public string Name => "MAC Address";

    public async Task<DiscoveredDevice> EnrichAsync(
        DiscoveredDevice device,
        NetworkScanContext context,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(device.MacAddress))
        {
            return device;
        }

        if (!IPAddress.TryParse(device.IpAddress, out var address))
        {
            return device;
        }

        if (!context.Options.AllowPublicRanges && !PrivateIpRangeHelper.IsPrivateIPv4(address))
        {
            return device;
        }

        var nativeMacAddress = TryGetMacAddressWithSendArp(address);
        if (!string.IsNullOrWhiteSpace(nativeMacAddress))
        {
            return device with { MacAddress = nativeMacAddress };
        }

        var entries = await ArpDiscoveryProvider.ReadArpEntriesAsync(cancellationToken).ConfigureAwait(false);
        var entry = entries.FirstOrDefault(candidate => string.Equals(candidate.IpAddress, device.IpAddress, StringComparison.OrdinalIgnoreCase));

        return entry is null
            ? device
            : device with { MacAddress = entry.MacAddress };
    }

    private static string? TryGetMacAddressWithSendArp(IPAddress address)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var destination = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
            var mac = new byte[6];
            var length = mac.Length;

            var result = SendARP(destination, 0, mac, ref length);
            if (result != 0 || length <= 0)
            {
                return null;
            }

            return string.Join(":", mac.Take(length).Select(value => value.ToString("X2")));
        }
        catch
        {
            return null;
        }
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref int physicalAddrLength);
}
