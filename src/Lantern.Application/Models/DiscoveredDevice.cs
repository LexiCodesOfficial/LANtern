namespace Lantern.Application;

using Lantern.Domain;

public sealed record DiscoveredDevice
{
    public required string IpAddress { get; init; }
    public string? MacAddress { get; init; }
    public string? Hostname { get; init; }
    public HostnameSource HostnameSource { get; init; }
    public string? Vendor { get; init; }
    public IReadOnlyCollection<OpenPortInfo> OpenPorts { get; init; } = [];
    public string DiscoverySource { get; init; } = "Unknown";
    public DateTimeOffset ObservedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DeviceClassification? Classification { get; init; }

    public string StableKey => string.IsNullOrWhiteSpace(MacAddress)
        ? $"ip:{IpAddress}"
        : $"mac:{MacAddress.ToUpperInvariant()}";

    public DiscoveredDevice Merge(DiscoveredDevice other)
    {
        var ports = OpenPorts.Concat(other.OpenPorts)
            .GroupBy(port => port.Port)
            .Select(group => group.OrderByDescending(port => port.ObservedUtc).First())
            .OrderBy(port => port.Port)
            .ToArray();

        return this with
        {
            MacAddress = Prefer(MacAddress, other.MacAddress),
            Hostname = PreferHostname(this, other),
            HostnameSource = PreferHostnameSource(this, other),
            Vendor = Prefer(Vendor, other.Vendor),
            OpenPorts = ports,
            DiscoverySource = DiscoverySource == other.DiscoverySource ? DiscoverySource : $"{DiscoverySource}, {other.DiscoverySource}",
            ObservedUtc = other.ObservedUtc > ObservedUtc ? other.ObservedUtc : ObservedUtc,
            Classification = other.Classification ?? Classification
        };
    }

    private static string? Prefer(string? current, string? candidate)
        => string.IsNullOrWhiteSpace(current) ? candidate : current;

    private static string? PreferHostname(DiscoveredDevice current, DiscoveredDevice candidate)
        => ShouldPreferHostname(candidate, current) ? candidate.Hostname : current.Hostname;

    private static HostnameSource PreferHostnameSource(DiscoveredDevice current, DiscoveredDevice candidate)
        => ShouldPreferHostname(candidate, current) ? candidate.HostnameSource : current.HostnameSource;

    private static bool ShouldPreferHostname(DiscoveredDevice candidate, DiscoveredDevice current)
        => !string.IsNullOrWhiteSpace(candidate.Hostname)
            && (string.IsNullOrWhiteSpace(current.Hostname) || candidate.HostnameSource > current.HostnameSource);
}
