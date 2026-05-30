# LANtern Scanner Pipeline

LANtern now uses a staged scanner pipeline instead of a single ping-first pass.

## Architecture

- `INetworkScanPipeline` streams `NetworkScanUpdate` values.
- `IDiscoveryProvider` finds devices quickly and emits partial `DiscoveredDevice` records.
- `IEnrichmentProvider` fills in slower details after a device is already visible.
- `IDeviceClassificationProvider` turns technical clues into friendly device types and explanations.
- `DeviceInventoryService.StreamScanAsync` loads known SQLite devices first, consumes pipeline updates, saves inventory continuously, and emits UI-ready updates.
- `LocalNetworkScanner` remains as a compatibility facade for code that still wants one final `DeviceObservation` list.

## Provider Order

1. `ArpDiscoveryProvider`
   - Reads local ARP/neighbour cache first.
   - Emits devices with MAC addresses immediately.
   - Uses `arp -a` on Windows/macOS and `ip neigh` on Linux.

2. `PingDiscoveryProvider`
   - Bounded concurrent fallback for devices not already visible through ARP.
   - Short timeout and cancellation-aware.

3. `TcpProbeDiscoveryProvider`
   - Conservative optional discovery for devices that block ping.
   - Disabled by default in `NetworkScanOptions.SafeDefault`.
   - Probes only selected local subnet addresses and common home-network ports.

4. `SsdpDiscoveryProvider` and `MdnsServiceDiscoveryProvider`
   - Send one conservative local multicast browse request each.
   - Emit private-LAN responders without treating service banners as hostnames.

## Enrichment

After discovery, devices are enriched progressively:

- `VendorEnrichmentProvider` uses the official cached IEEE OUI database, imported CSV data when present, and a built-in fallback table.
- `HostnameEnrichmentProvider` performs reverse DNS with a short timeout and failed-lookup cache.
- On Windows, `HostnameEnrichmentProvider` also tries a short NetBIOS lookup with `nbtstat -A`.
- `PortProbeEnrichmentProvider` checks common local service ports with bounded concurrency.
- `DeviceClassificationProvider` classifies devices from hostname, vendor, MAC prefix, open ports, and previous saved inventory context when available through merge behavior.

## Hostname Notes

Routers such as MikroTik may show an "Active Host Name" from their DHCP lease table. When enabled in Settings, LANtern reads those DHCP lease names from the RouterOS API with a user-provided read-only account. Jellyfin classification uses port `8096`; it does not manufacture a `jellyfin` hostname.

## Safety

LANtern is intentionally a LAN inventory tool:

- Private RFC1918 IPv4 ranges only by default.
- Large subnets are capped to a `/24` by default.
- TCP discovery is disabled by default and remains conservative when enabled.
- All network work uses bounded concurrency and short timeouts.
- No public IP scanning unless a future explicit advanced option sets `AllowPublicRanges`.
- No raw packet blasting or internet-scale scanning behavior.

## Avalonia ViewModel Usage

```csharp
await foreach (var update in inventory.StreamScanAsync())
{
    ProgressMessage = update.Message;

    if (update.UpdateType is NetworkScanUpdateType.KnownDeviceLoaded
        or NetworkScanUpdateType.DeviceUpdated
        or NetworkScanUpdateType.DeviceEnriched
        or NetworkScanUpdateType.DeviceClassified
        or NetworkScanUpdateType.ScanCompleted)
    {
        await ReloadDeviceCardsAsync();
    }
}
```

## Suggested Tests

- ARP parser handles Windows, Linux `ip neigh`, and macOS `arp -a` formats.
- Public IP candidates are excluded unless `AllowPublicRanges` is true.
- Large subnets are capped to `MaxHostsPerInterface`.
- Ping discovery emits devices before the full candidate list completes.
- TCP discovery is skipped when disabled.
- Vendor lookup returns cached values and maps misses to `Unknown Vendor`.
- Hostname lookup failures are cached briefly.
- Pipeline deduplicates by MAC address first, then IP address.
- Inventory stream emits known devices before discovery updates.
- Device changes create one timeline event per meaningful change during a scan.
