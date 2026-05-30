using Lantern.Application;
using Lantern.Application.Abstractions;
using Lantern.Domain;

namespace Lantern.Infrastructure.NetworkScanning.Enrichment;

public sealed class DhcpLeaseHostnameEnrichmentProvider : IEnrichmentProvider
{
    private static readonly TimeSpan LeaseCacheDuration = TimeSpan.FromSeconds(20);
    private readonly IEnumerable<IDhcpLeaseProvider> _leaseProviders;
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private IReadOnlyList<DhcpLeaseRecord> _cachedLeases = [];
    private DateTimeOffset _cacheExpiresUtc;

    public DhcpLeaseHostnameEnrichmentProvider(IEnumerable<IDhcpLeaseProvider> leaseProviders)
    {
        _leaseProviders = leaseProviders;
    }

    public string Name => "DHCP Lease Hostname";

    public async Task<DiscoveredDevice> EnrichAsync(
        DiscoveredDevice device,
        NetworkScanContext context,
        CancellationToken cancellationToken)
    {
        var leases = await GetLeasesAsync(cancellationToken).ConfigureAwait(false);
        var lease = leases.FirstOrDefault(candidate => Matches(device, candidate) && !string.IsNullOrWhiteSpace(candidate.Hostname));
        if (lease is not null)
        {
            return device with
            {
                Hostname = lease.Hostname,
                HostnameSource = HostnameSource.DhcpLease,
                MacAddress = string.IsNullOrWhiteSpace(device.MacAddress) ? lease.MacAddress : device.MacAddress,
                DiscoverySource = $"{device.DiscoverySource}, {lease.Source}"
            };
        }

        return device;
    }

    private async Task<IReadOnlyList<DhcpLeaseRecord>> GetLeasesAsync(CancellationToken cancellationToken)
    {
        if (_cacheExpiresUtc > DateTimeOffset.UtcNow)
        {
            return _cachedLeases;
        }

        await _cacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cacheExpiresUtc > DateTimeOffset.UtcNow)
            {
                return _cachedLeases;
            }

            var leases = new List<DhcpLeaseRecord>();
            foreach (var provider in _leaseProviders.Where(provider => provider.IsEnabled))
            {
                try
                {
                    leases.AddRange(await provider.GetLeasesAsync(cancellationToken).ConfigureAwait(false));
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                }
            }

            _cachedLeases = leases;
            _cacheExpiresUtc = DateTimeOffset.UtcNow.Add(LeaseCacheDuration);
            return _cachedLeases;
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    private static bool Matches(DiscoveredDevice device, DhcpLeaseRecord lease)
    {
        if (!string.IsNullOrWhiteSpace(device.MacAddress) && !string.IsNullOrWhiteSpace(lease.MacAddress) &&
            string.Equals(NormalizeMac(device.MacAddress), NormalizeMac(lease.MacAddress), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(device.IpAddress, lease.IpAddress, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMac(string value)
        => new(value.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
}
