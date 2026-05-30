using Lantern.Application;
using Lantern.Application.Abstractions;
using Lantern.Domain;

namespace Lantern.Infrastructure.Networking;

public sealed class LocalNetworkScanner : INetworkScanner
{
    private readonly INetworkScanPipeline _pipeline;

    public LocalNetworkScanner(INetworkScanPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public async Task<IReadOnlyList<DeviceObservation>> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var devices = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);

        await foreach (var update in _pipeline.ScanAsync(NetworkScanOptions.SafeDefault, cancellationToken).ConfigureAwait(false))
        {
            if (update.Completed is not null || update.Total is not null)
            {
                progress?.Report(new ScanProgress(update.Message, update.Completed ?? 0, update.Total ?? 0));
            }

            if (update.Device is not null)
            {
                devices[update.Device.StableKey] = update.Device;
            }
        }

        return devices.Values
            .Select(device => new DeviceObservation(
                device.IpAddress,
                device.MacAddress,
                device.Hostname,
                device.Vendor == "Unknown Vendor" ? null : device.Vendor,
                device.OpenPorts.Select(port => port.Port).ToArray(),
                device.ObservedUtc))
            .ToArray();
    }
}
