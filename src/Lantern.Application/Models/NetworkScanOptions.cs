namespace Lantern.Application;

public sealed record NetworkScanOptions
{
    public bool AllowPublicRanges { get; init; }
    public int MaxHostsPerInterface { get; init; } = 254;
    public int PingConcurrency { get; init; } = 64;
    public int TcpDiscoveryConcurrency { get; init; } = 24;
    public int PortEnrichmentConcurrency { get; init; } = 6;
    public TimeSpan PingTimeout { get; init; } = TimeSpan.FromMilliseconds(450);
    public TimeSpan TcpConnectTimeout { get; init; } = TimeSpan.FromMilliseconds(220);
    public TimeSpan HostnameTimeout { get; init; } = TimeSpan.FromMilliseconds(650);
    public bool EnableTcpDiscovery { get; init; }
    public bool EnablePortEnrichment { get; init; } = true;
    public IReadOnlyCollection<int> TcpDiscoveryPorts { get; init; } = [22, 80, 443, 445, 3389, 8080, 8096, 8123, 9090, 9443, 3000, 5000];
    public IReadOnlyCollection<int> EnrichmentPorts { get; init; } = [22, 80, 443, 445, 554, 8006, 1883, 3389, 8009, 8080, 8096, 8123, 9090, 9443, 3000, 5000, 9100];

    public static NetworkScanOptions SafeDefault { get; } = new();
}
