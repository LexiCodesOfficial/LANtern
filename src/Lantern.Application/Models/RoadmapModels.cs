namespace Lantern.Application;

public enum ScanProfile
{
    Home,
    Homelab,
    Classroom
}

public sealed record IntegrationSummary(
    string Name,
    string Status,
    string Explanation,
    string? Address = null);

public static class ScanProfileOptions
{
    public static NetworkScanOptions Create(ScanProfile profile) => profile switch
    {
        ScanProfile.Homelab => NetworkScanOptions.SafeDefault with
        {
            EnableTcpDiscovery = true,
            PingConcurrency = 72,
            TcpDiscoveryConcurrency = 24,
            PortEnrichmentConcurrency = 8
        },
        ScanProfile.Classroom => NetworkScanOptions.SafeDefault with
        {
            EnableTcpDiscovery = false,
            PingConcurrency = 40,
            PortEnrichmentConcurrency = 4,
            MaxHostsPerInterface = 254
        },
        _ => NetworkScanOptions.SafeDefault
    };
}
