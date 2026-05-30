using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Lantern.Application;
using Lantern.Application.Abstractions;
using Lantern.Application.Utilities;

namespace Lantern.Infrastructure.NetworkScanning.Discovery;

public sealed class ArpDiscoveryProvider : IDiscoveryProvider
{
    public string Name => "ARP";

    public async IAsyncEnumerable<DiscoveredDevice> DiscoverAsync(
        NetworkScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var candidates = context.CandidateAddresses.Select(address => address.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in await ReadArpEntriesAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IPAddress.TryParse(entry.IpAddress, out var address))
            {
                continue;
            }

            if (!context.Options.AllowPublicRanges && !PrivateIpRangeHelper.IsPrivateIPv4(address))
            {
                continue;
            }

            if (candidates.Count > 0 && !candidates.Contains(entry.IpAddress))
            {
                continue;
            }

            yield return new DiscoveredDevice
            {
                IpAddress = entry.IpAddress,
                MacAddress = entry.MacAddress,
                DiscoverySource = Name,
                ObservedUtc = DateTimeOffset.UtcNow
            };
        }
    }

    public static async Task<IReadOnlyList<ArpEntry>> ReadArpEntriesAsync(CancellationToken cancellationToken)
    {
        var fileName = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? "arp" : "ip";
        var arguments = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? "-a" : "neigh";

        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return [];
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return Parse(output);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }

    public static IReadOnlyList<ArpEntry> Parse(string output)
    {
        var entries = new List<ArpEntry>();

        foreach (Match match in Regex.Matches(output, @"(?<ip>(?:\d{1,3}\.){3}\d{1,3}).{1,80}?(?<mac>(?:[0-9a-fA-F]{2}[:-]){5}[0-9a-fA-F]{2})"))
        {
            var ip = match.Groups["ip"].Value;
            var mac = NormalizeMac(match.Groups["mac"].Value);

            if (IPAddress.TryParse(ip, out _) && !string.Equals(mac, "FF:FF:FF:FF:FF:FF", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new ArpEntry(ip, mac));
            }
        }

        return entries.DistinctBy(entry => $"{entry.IpAddress}|{entry.MacAddress}").ToArray();
    }

    private static string NormalizeMac(string value)
    {
        var hex = new string(value.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
        return hex.Length < 12
            ? value.ToUpperInvariant()
            : string.Join(":", Enumerable.Range(0, 6).Select(index => hex.Substring(index * 2, 2)));
    }

    public sealed record ArpEntry(string IpAddress, string MacAddress);
}
