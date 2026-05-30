using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Lantern.Application;
using Lantern.Application.Abstractions;

namespace Lantern.Infrastructure.NetworkScanning;

public sealed class NetworkScanPipeline : INetworkScanPipeline
{
    private readonly IReadOnlyList<IDiscoveryProvider> _discoveryProviders;
    private readonly IReadOnlyList<IEnrichmentProvider> _enrichmentProviders;
    private readonly IDeviceClassificationProvider _classificationProvider;

    public NetworkScanPipeline(
        IEnumerable<IDiscoveryProvider> discoveryProviders,
        IEnumerable<IEnrichmentProvider> enrichmentProviders,
        IDeviceClassificationProvider classificationProvider)
    {
        _discoveryProviders = discoveryProviders.ToArray();
        _enrichmentProviders = enrichmentProviders.ToArray();
        _classificationProvider = classificationProvider;
    }

    public async IAsyncEnumerable<NetworkScanUpdate> ScanAsync(
        NetworkScanOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var context = NetworkInterfaceContextBuilder.Build(options);
        var devices = new ConcurrentDictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);

        yield return new NetworkScanUpdate(
            NetworkScanUpdateType.ScanStarted,
            null,
            $"Scanning {context.CandidateAddresses.Count} local addresses",
            context.StartedUtc,
            0,
            context.CandidateAddresses.Count);

        if (context.CandidateAddresses.Count == 0)
        {
            yield return new NetworkScanUpdate(NetworkScanUpdateType.ScanCompleted, null, "No private local subnet was found.", DateTimeOffset.UtcNow, 0, 0);
            yield break;
        }

        var enrichmentChannel = Channel.CreateBounded<DiscoveredDevice>(new BoundedChannelOptions(256)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        var updateChannel = Channel.CreateUnbounded<NetworkScanUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var enrichmentTask = RunEnrichmentAsync(context, enrichmentChannel.Reader, updateChannel.Writer, devices, cancellationToken);

        try
        {
            foreach (var provider in _discoveryProviders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return NetworkScanUpdate.Progress($"Running {provider.Name} discovery");

                await foreach (var discovered in provider.DiscoverAsync(context, cancellationToken).ConfigureAwait(false))
                {
                    var merged = AddOrMerge(devices, discovered);

                    await updateChannel.Writer.WriteAsync(new NetworkScanUpdate(
                        NetworkScanUpdateType.DeviceDiscovered,
                        merged,
                        $"{provider.Name} found {merged.IpAddress}",
                        DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);

                    await enrichmentChannel.Writer.WriteAsync(merged, cancellationToken).ConfigureAwait(false);
                    DrainUpdates(updateChannel.Reader, out var drainedUpdates);
                    foreach (var update in drainedUpdates)
                    {
                        yield return update;
                    }
                }
            }
        }
        finally
        {
            enrichmentChannel.Writer.TryComplete();
        }

        await enrichmentTask.ConfigureAwait(false);
        updateChannel.Writer.TryComplete();

        await foreach (var update in updateChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }

        yield return new NetworkScanUpdate(
            NetworkScanUpdateType.ScanCompleted,
            null,
            $"Scan complete. Found {devices.Count} local devices.",
            DateTimeOffset.UtcNow,
            devices.Count,
            devices.Count);
    }

    private async Task RunEnrichmentAsync(
        NetworkScanContext context,
        ChannelReader<DiscoveredDevice> devicesToEnrich,
        ChannelWriter<NetworkScanUpdate> updates,
        ConcurrentDictionary<string, DiscoveredDevice> devices,
        CancellationToken cancellationToken)
    {
        var workers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await foreach (var device in devicesToEnrich.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var enriched = device;

                foreach (var provider in _enrichmentProviders)
                {
                    enriched = await provider.EnrichAsync(enriched, context, cancellationToken).ConfigureAwait(false);
                    enriched = AddOrMerge(devices, enriched);

                    await updates.WriteAsync(new NetworkScanUpdate(
                        NetworkScanUpdateType.DeviceEnriched,
                        enriched,
                        $"{provider.Name} updated {enriched.IpAddress}",
                        DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
                }

                var classification = _classificationProvider.Classify(enriched);
                enriched = AddOrMerge(devices, enriched with { Classification = classification });

                await updates.WriteAsync(new NetworkScanUpdate(
                    NetworkScanUpdateType.DeviceClassified,
                    enriched,
                    classification.Explanation,
                    DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken));

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private static DiscoveredDevice AddOrMerge(ConcurrentDictionary<string, DiscoveredDevice> devices, DiscoveredDevice device)
    {
        var ipKey = $"ip:{device.IpAddress}";
        if (!string.IsNullOrWhiteSpace(device.MacAddress) && devices.TryRemove(ipKey, out var existingByIp))
        {
            device = existingByIp.Merge(device);
        }

        if (string.IsNullOrWhiteSpace(device.MacAddress) && devices.Values.FirstOrDefault(existing => existing.IpAddress == device.IpAddress) is { } existingByAddress)
        {
            device = existingByAddress.Merge(device);
        }

        var key = device.StableKey;
        var merged = devices.AddOrUpdate(key, device, (_, existing) => existing.Merge(device));

        if (!string.IsNullOrWhiteSpace(device.MacAddress))
        {
            devices.TryRemove(ipKey, out _);
        }

        return merged;
    }

    private static void DrainUpdates(ChannelReader<NetworkScanUpdate> reader, out IReadOnlyList<NetworkScanUpdate> updates)
    {
        var result = new List<NetworkScanUpdate>();
        while (reader.TryRead(out var update))
        {
            result.Add(update);
        }

        updates = result;
    }
}
