namespace Lantern.Application.Abstractions;

public interface IDiscoveryProvider
{
    string Name { get; }

    IAsyncEnumerable<DiscoveredDevice> DiscoverAsync(
        NetworkScanContext context,
        CancellationToken cancellationToken);
}
