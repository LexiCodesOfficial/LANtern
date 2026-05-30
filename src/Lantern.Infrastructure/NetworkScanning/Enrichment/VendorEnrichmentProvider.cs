using System.Collections.Concurrent;
using Lantern.Application;
using Lantern.Application.Abstractions;

namespace Lantern.Infrastructure.NetworkScanning.Enrichment;

public sealed class VendorEnrichmentProvider : IEnrichmentProvider
{
    private readonly IVendorLookupService _vendorLookup;
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public VendorEnrichmentProvider(IVendorLookupService vendorLookup)
    {
        _vendorLookup = vendorLookup;
    }

    public string Name => "Vendor";

    public Task<DiscoveredDevice> EnrichAsync(
        DiscoveredDevice device,
        NetworkScanContext context,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(device.Vendor) || string.IsNullOrWhiteSpace(device.MacAddress))
        {
            return Task.FromResult(device);
        }

        var vendor = _cache.GetOrAdd(device.MacAddress, mac => _vendorLookup.LookupVendor(mac) ?? "Unknown Vendor");
        return Task.FromResult(device with { Vendor = vendor });
    }
}
