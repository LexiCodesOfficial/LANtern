using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Lantern.Application;
using Lantern.Application.Abstractions;
using Lantern.Domain;

namespace Lantern.Infrastructure.NetworkScanning.Enrichment;

public sealed class HostnameEnrichmentProvider : IEnrichmentProvider
{
    private static readonly TimeSpan FailedLookupCacheDuration = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _failedLookups = new(StringComparer.OrdinalIgnoreCase);

    public string Name => "Hostname";

    public async Task<DiscoveredDevice> EnrichAsync(
        DiscoveredDevice device,
        NetworkScanContext context,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(device.Hostname) || IsRecentlyFailed(device.IpAddress))
        {
            return device;
        }

        if (IPAddress.TryParse(device.IpAddress, out var address))
        {
            var mdnsTask = MulticastHostnameResolver.ResolveMdnsAsync(address, context.Options.HostnameTimeout, cancellationToken);
            var llmnrTask = MulticastHostnameResolver.ResolveLlmnrAsync(address, context.Options.HostnameTimeout, cancellationToken);
            var reverseDnsTask = TryResolveReverseDnsNameAsync(device.IpAddress, context.Options.HostnameTimeout, cancellationToken);
            await Task.WhenAll(mdnsTask, llmnrTask, reverseDnsTask).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(mdnsTask.Result))
            {
                return device with { Hostname = mdnsTask.Result, HostnameSource = HostnameSource.MulticastDns };
            }

            if (!string.IsNullOrWhiteSpace(llmnrTask.Result))
            {
                return device with { Hostname = llmnrTask.Result, HostnameSource = HostnameSource.Llmnr };
            }

            if (!string.IsNullOrWhiteSpace(reverseDnsTask.Result))
            {
                return device with { Hostname = reverseDnsTask.Result, HostnameSource = HostnameSource.ReverseDns };
            }
        }

        var netBiosName = await TryResolveNetBiosNameAsync(device.IpAddress, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(netBiosName))
        {
            return device with { Hostname = netBiosName, HostnameSource = HostnameSource.NetBios };
        }

        var pingName = await TryResolvePingNameAsync(device.IpAddress, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(pingName))
        {
            return device with { Hostname = pingName, HostnameSource = HostnameSource.ReverseDns };
        }

        _failedLookups[device.IpAddress] = DateTimeOffset.UtcNow;
        return device;
    }

    private bool IsRecentlyFailed(string ipAddress)
        => _failedLookups.TryGetValue(ipAddress, out var failedUtc)
            && DateTimeOffset.UtcNow - failedUtc < FailedLookupCacheDuration;

    private static async Task<string?> TryResolveReverseDnsNameAsync(string ipAddress, TimeSpan timeoutDuration, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutDuration);
            var entry = await Dns.GetHostEntryAsync(ipAddress, timeout.Token).ConfigureAwait(false);
            return CleanHostname(entry.HostName);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string? CleanHostname(string? hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname) || IPAddress.TryParse(hostname, out _))
        {
            return null;
        }

        var trimmed = hostname.Trim().TrimEnd('.');
        var firstLabel = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstLabel) ? null : firstLabel;
    }

    private static async Task<string?> TryResolveNetBiosNameAsync(string ipAddress, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("nbtstat", $"-A {ipAddress}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(900));

            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            foreach (Match match in Regex.Matches(output, @"^\s*(?<name>[A-Z0-9][A-Z0-9_-]{0,14})\s+<00>\s+UNIQUE", RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                var name = match.Groups["name"].Value.Trim();
                if (!string.Equals(name, "WORKGROUP", StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return null;
    }

    private static async Task<string?> TryResolvePingNameAsync(string ipAddress, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("ping", $"-a -n 1 -w 650 {ipAddress}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(900));

            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            var match = Regex.Match(output, $@"Pinging\s+(?<name>[^\s\[]+)\s+\[{Regex.Escape(ipAddress)}\]", RegexOptions.IgnoreCase);
            return match.Success ? CleanHostname(match.Groups["name"].Value) : null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
