namespace Lantern.Application.Abstractions;

public interface IEnrichmentProvider
{
    string Name { get; }

    Task<DiscoveredDevice> EnrichAsync(
        DiscoveredDevice device,
        NetworkScanContext context,
        CancellationToken cancellationToken);
}
