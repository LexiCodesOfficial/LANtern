using Lantern.Application.Abstractions;
using Lantern.Domain;

namespace Lantern.Application.Services;

public sealed class DeviceInventoryService
{
    private static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(10);
    private readonly IDeviceRepository _repository;
    private readonly INetworkScanPipeline _scanPipeline;
    private readonly IClock _clock;
    private readonly INotificationService _notifications;

    public DeviceInventoryService(
        IDeviceRepository repository,
        INetworkScanPipeline scanPipeline,
        IClock clock,
        INotificationService notifications)
    {
        _repository = repository;
        _scanPipeline = scanPipeline;
        _clock = clock;
        _notifications = notifications;
    }

    public async Task<ScanSummary> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var events = new List<NetworkEvent>();
        var observedDevices = new HashSet<Guid>();
        var newDevices = 0;
        var onlineDevices = 0;
        var offlineDevices = 0;

        await foreach (var update in StreamScanAsync(NetworkScanOptions.SafeDefault, cancellationToken).ConfigureAwait(false))
        {
            if (update.Completed is not null || update.Total is not null)
            {
                progress?.Report(new ScanProgress(update.Message, update.Completed ?? 0, update.Total ?? 0));
            }
            if (update.Event is not null)
            {
                events.Add(update.Event);
                if (update.Event.Kind == NetworkEventKind.NewDeviceJoined)
                {
                    newDevices++;
                }
            }
            if (update.InventoryDevice is not null)
            {
                observedDevices.Add(update.InventoryDevice.Id);
            }
            if (update.UpdateType == NetworkScanUpdateType.ScanCompleted)
            {
                var refreshed = await _repository.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
                onlineDevices = refreshed.Count(device => device.Status == DeviceStatus.Online);
                offlineDevices = refreshed.Count(device => device.Status == DeviceStatus.Offline);
            }
        }

        return new ScanSummary(
            observedDevices.Count,
            newDevices,
            onlineDevices,
            offlineDevices,
            events);
    }

    public async IAsyncEnumerable<NetworkScanUpdate> StreamScanAsync(
        NetworkScanOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _repository.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var knownDevices = await _repository.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        var seenDeviceIds = new HashSet<Guid>();
        var emittedEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        yield return new NetworkScanUpdate(NetworkScanUpdateType.ScanStarted, null, "Loading known devices", _clock.UtcNow, 0, knownDevices.Count);

        var loaded = 0;
        foreach (var knownDevice in knownDevices)
        {
            loaded++;
            yield return new NetworkScanUpdate(
                NetworkScanUpdateType.KnownDeviceLoaded,
                ToDiscoveredDevice(knownDevice),
                $"{knownDevice.DisplayName} loaded from memory",
                _clock.UtcNow,
                loaded,
                knownDevices.Count,
                knownDevice);
        }

        await foreach (var update in _scanPipeline.ScanAsync(options ?? NetworkScanOptions.SafeDefault, cancellationToken).ConfigureAwait(false))
        {
            if (update.Device is null)
            {
                yield return update;
                continue;
            }

            var (device, events) = await UpsertDiscoveredDeviceAsync(
                update.Device,
                update.UpdateType == NetworkScanUpdateType.DeviceClassified,
                cancellationToken).ConfigureAwait(false);
            seenDeviceIds.Add(device.Id);

            NetworkEvent? firstEvent = null;
            foreach (var networkEvent in events)
            {
                var key = $"{networkEvent.Kind}:{networkEvent.DeviceId}:{networkEvent.Description}";
                if (!emittedEvents.Add(key))
                {
                    continue;
                }

                await _repository.AddEventAsync(networkEvent, cancellationToken).ConfigureAwait(false);
                if (networkEvent.Kind is NetworkEventKind.NewDeviceJoined or NetworkEventKind.DeviceDisappeared or NetworkEventKind.DeviceReturnedOnline)
                {
                    await _notifications.NotifyAsync(networkEvent, cancellationToken).ConfigureAwait(false);
                }

                firstEvent ??= networkEvent;
            }

            yield return update with
            {
                UpdateType = update.UpdateType == NetworkScanUpdateType.DeviceDiscovered
                    ? NetworkScanUpdateType.DeviceUpdated
                    : update.UpdateType,
                InventoryDevice = device,
                Event = firstEvent
            };
        }

        var allDevices = await _repository.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var device in allDevices.Where(device => !seenDeviceIds.Contains(device.Id) && device.Status == DeviceStatus.Online && _clock.UtcNow - device.LastSeenUtc > OfflineAfter))
        {
            device.Status = DeviceStatus.Offline;
            await _repository.SaveDeviceAsync(device, cancellationToken).ConfigureAwait(false);
            var networkEvent = NewEvent(device.Id, NetworkEventKind.DeviceDisappeared, "Device Went Offline", $"{device.DisplayName} has not answered recent scans.", _clock.UtcNow);
            await _repository.AddEventAsync(networkEvent, cancellationToken).ConfigureAwait(false);
            await _notifications.NotifyAsync(networkEvent, cancellationToken).ConfigureAwait(false);

            yield return new NetworkScanUpdate(
                NetworkScanUpdateType.DeviceUpdated,
                ToDiscoveredDevice(device),
                $"{device.DisplayName} is now offline",
                _clock.UtcNow,
                InventoryDevice: device,
                Event: networkEvent);
        }

        yield return new NetworkScanUpdate(NetworkScanUpdateType.ScanCompleted, null, "Scan complete", _clock.UtcNow, seenDeviceIds.Count, seenDeviceIds.Count);
    }

    public async Task<IReadOnlyList<NetworkDevice>> GetDevicesAsync(DeviceFilter filter, CancellationToken cancellationToken = default)
    {
        await _repository.InitializeAsync(cancellationToken);
        var devices = await _repository.GetDevicesAsync(cancellationToken);
        var query = devices.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            query = query.Where(device => Contains(device.FriendlyName, filter.SearchText)
                || Contains(device.Hostname, filter.SearchText)
                || Contains(device.LastIpAddress, filter.SearchText)
                || Contains(device.MacAddress, filter.SearchText)
                || Contains(device.Vendor, filter.SearchText));
        }

        if (filter.Status is not null)
        {
            query = query.Where(device => device.Status == filter.Status);
        }

        if (filter.DeviceType is not null)
        {
            query = query.Where(device => device.DeviceType == filter.DeviceType);
        }

        if (!string.IsNullOrWhiteSpace(filter.Vendor))
        {
            query = query.Where(device => string.Equals(device.Vendor, filter.Vendor, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.UnknownOnly)
        {
            query = query.Where(device => device.DeviceType == DeviceType.UnknownDevice);
        }

        if (filter.RecentlySeenOnly)
        {
            var cutoff = _clock.UtcNow.AddHours(-24);
            query = query.Where(device => device.LastSeenUtc >= cutoff);
        }

        return query.OrderByDescending(device => device.Status == DeviceStatus.Online)
            .ThenBy(device => device.DisplayName)
            .ToList();
    }

    public Task<IReadOnlyList<NetworkEvent>> GetTimelineAsync(Guid? deviceId = null, int take = 100, CancellationToken cancellationToken = default)
        => _repository.GetEventsAsync(deviceId, take, cancellationToken);

    public async Task RenameDeviceAsync(Guid deviceId, string friendlyName, CancellationToken cancellationToken = default)
    {
        var device = await _repository.GetDeviceAsync(deviceId, cancellationToken);
        if (device is null)
        {
            return;
        }

        device.FriendlyName = string.IsNullOrWhiteSpace(friendlyName) ? BuildFriendlyName(device) : friendlyName.Trim();
        device.IsUserNamed = !string.IsNullOrWhiteSpace(friendlyName);
        await _repository.SaveDeviceAsync(device, cancellationToken);
        await _repository.AddEventAsync(NewEvent(device.Id, NetworkEventKind.DeviceRenamed, "Device Renamed", $"This device is now called {device.DisplayName}.", _clock.UtcNow), cancellationToken);
    }

    public async Task SaveNotesAsync(Guid deviceId, string notes, CancellationToken cancellationToken = default)
    {
        var device = await _repository.GetDeviceAsync(deviceId, cancellationToken);
        if (device is null)
        {
            return;
        }

        device.Notes = notes;
        await _repository.SaveDeviceAsync(device, cancellationToken);
    }

    public async Task SaveLocationLabelAsync(Guid deviceId, string locationLabel, CancellationToken cancellationToken = default)
    {
        var device = await _repository.GetDeviceAsync(deviceId, cancellationToken);
        if (device is null)
        {
            return;
        }

        device.LocationLabel = locationLabel.Trim();
        await _repository.SaveDeviceAsync(device, cancellationToken);
    }

    private async Task<NetworkDevice?> FindExistingDeviceAsync(DeviceObservation observation, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(observation.MacAddress))
        {
            var byMac = await _repository.FindByMacAddressAsync(observation.MacAddress, cancellationToken);
            if (byMac is not null)
            {
                return byMac;
            }
        }

        return await _repository.FindByIpAddressAsync(observation.IpAddress, cancellationToken);
    }

    private async Task<NetworkDevice?> FindExistingDeviceAsync(DiscoveredDevice discoveredDevice, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(discoveredDevice.MacAddress))
        {
            var byMac = await _repository.FindByMacAddressAsync(discoveredDevice.MacAddress, cancellationToken).ConfigureAwait(false);
            if (byMac is not null)
            {
                return byMac;
            }
        }

        return await _repository.FindByIpAddressAsync(discoveredDevice.IpAddress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(NetworkDevice Device, IReadOnlyList<NetworkEvent> Events)> UpsertDiscoveredDeviceAsync(
        DiscoveredDevice discoveredDevice,
        bool classificationIsAuthoritative,
        CancellationToken cancellationToken)
    {
        var events = new List<NetworkEvent>();
        var device = await FindExistingDeviceAsync(discoveredDevice, cancellationToken).ConfigureAwait(false);

        if (device is null)
        {
            device = CreateDevice(discoveredDevice);
            events.Add(NewEvent(device.Id, NetworkEventKind.NewDeviceJoined, "New Device Detected", DescribeNewDevice(device), discoveredDevice.ObservedUtc));
        }
        else
        {
            ApplyChanges(device, discoveredDevice, events);
        }

        if (IsLegacySyntheticHostname(device.Hostname, discoveredDevice.Hostname))
        {
            device.Hostname = null;
            device.HostnameSource = HostnameSource.Unknown;
        }

        device.MacAddress = Prefer(device.MacAddress, discoveredDevice.MacAddress);
        ApplyHostname(device, discoveredDevice);
        device.Vendor = Prefer(NormalizeVendor(discoveredDevice.Vendor), device.Vendor);
        device.LastIpAddress = discoveredDevice.IpAddress;
        device.LastSeenUtc = discoveredDevice.ObservedUtc;
        device.Status = DeviceStatus.Online;

        if (discoveredDevice.Classification is { } classification &&
            (classificationIsAuthoritative || device.DeviceType == DeviceType.UnknownDevice || classification.Confidence >= 0.65))
        {
            device.DeviceType = classification.DeviceType;
            device.ClassificationExplanation = classification.Explanation;
        }

        if (!device.IsUserNamed)
        {
            device.FriendlyName = BuildFriendlyName(device);
        }

        await _repository.SaveDeviceAsync(device, cancellationToken).ConfigureAwait(false);
        await _repository.ReplaceOpenPortsAsync(device.Id, discoveredDevice.OpenPorts.Select(port => port.Port), discoveredDevice.ObservedUtc, cancellationToken).ConfigureAwait(false);
        return (device, events);
    }

    private static NetworkDevice CreateDevice(DeviceObservation observation)
        => new()
        {
            Id = Guid.NewGuid(),
            FriendlyName = "Unknown Device",
            Hostname = observation.Hostname,
            MacAddress = observation.MacAddress,
            Vendor = observation.Vendor,
            FirstSeenUtc = observation.SeenUtc,
            LastSeenUtc = observation.SeenUtc,
            Status = DeviceStatus.Online,
            LastIpAddress = observation.IpAddress
        };

    private static NetworkDevice CreateDevice(DiscoveredDevice discoveredDevice)
        => new()
        {
            Id = Guid.NewGuid(),
            FriendlyName = "Unknown Device",
            Hostname = discoveredDevice.Hostname,
            HostnameSource = discoveredDevice.HostnameSource,
            MacAddress = discoveredDevice.MacAddress,
            Vendor = NormalizeVendor(discoveredDevice.Vendor),
            DeviceType = discoveredDevice.Classification?.DeviceType ?? DeviceType.UnknownDevice,
            ClassificationExplanation = discoveredDevice.Classification?.Explanation ?? "LANtern has not seen enough clues to identify this device yet.",
            FirstSeenUtc = discoveredDevice.ObservedUtc,
            LastSeenUtc = discoveredDevice.ObservedUtc,
            Status = DeviceStatus.Online,
            LastIpAddress = discoveredDevice.IpAddress
        };

    private void ApplyChanges(NetworkDevice device, DeviceObservation observation, List<NetworkEvent> events)
    {
        if (device.Status == DeviceStatus.Offline)
        {
            events.Add(NewEvent(device.Id, NetworkEventKind.DeviceReturnedOnline, "Device Returned", $"{device.DisplayName} is online again.", observation.SeenUtc));
        }

        if (!string.IsNullOrWhiteSpace(device.LastIpAddress) && device.LastIpAddress != observation.IpAddress)
        {
            events.Add(NewEvent(device.Id, NetworkEventKind.DeviceChangedIp, "Device Changed IP", $"{device.DisplayName} moved from {device.LastIpAddress} to {observation.IpAddress}.", observation.SeenUtc));
        }

        if (!string.IsNullOrWhiteSpace(observation.Hostname) && !string.IsNullOrWhiteSpace(device.Hostname) && !string.Equals(device.Hostname, observation.Hostname, StringComparison.OrdinalIgnoreCase))
        {
            events.Add(NewEvent(device.Id, NetworkEventKind.DeviceChangedHostname, "Hostname Changed", $"{device.DisplayName} changed hostname from {device.Hostname} to {observation.Hostname}.", observation.SeenUtc));
        }
    }

    private void ApplyChanges(NetworkDevice device, DiscoveredDevice discoveredDevice, List<NetworkEvent> events)
    {
        if (device.Status == DeviceStatus.Offline)
        {
            events.Add(NewEvent(device.Id, NetworkEventKind.DeviceReturnedOnline, "Device Returned", $"{device.DisplayName} is online again.", discoveredDevice.ObservedUtc));
        }

        if (!string.IsNullOrWhiteSpace(device.LastIpAddress) && device.LastIpAddress != discoveredDevice.IpAddress)
        {
            events.Add(NewEvent(device.Id, NetworkEventKind.DeviceChangedIp, "Device Changed IP", $"{device.DisplayName} moved from {device.LastIpAddress} to {discoveredDevice.IpAddress}.", discoveredDevice.ObservedUtc));
        }

        if (!string.IsNullOrWhiteSpace(discoveredDevice.Hostname) && !string.IsNullOrWhiteSpace(device.Hostname) && !string.Equals(device.Hostname, discoveredDevice.Hostname, StringComparison.OrdinalIgnoreCase))
        {
            events.Add(NewEvent(device.Id, NetworkEventKind.DeviceChangedHostname, "Hostname Changed", $"{device.DisplayName} changed hostname from {device.Hostname} to {discoveredDevice.Hostname}.", discoveredDevice.ObservedUtc));
        }
    }

    private static NetworkEvent NewEvent(Guid deviceId, NetworkEventKind kind, string title, string description, DateTimeOffset occurredUtc)
        => new(Guid.NewGuid(), deviceId, kind, title, description, occurredUtc);

    private static string DescribeNewDevice(NetworkDevice device)
    {
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(device.Vendor))
        {
            details.Add($"Vendor: {device.Vendor}");
        }

        if (!string.IsNullOrWhiteSpace(device.LastIpAddress))
        {
            details.Add($"IP: {device.LastIpAddress}");
        }

        return details.Count == 0 ? "LANtern found a device it has not seen before." : string.Join(Environment.NewLine, details);
    }

    private static string BuildFriendlyName(NetworkDevice device)
    {
        if (!string.IsNullOrWhiteSpace(device.Hostname) && !IsMisleadingServiceAlias(device))
        {
            return device.Hostname;
        }

        return device.DeviceType == DeviceType.UnknownDevice ? "Unknown Device" : ToFriendlyType(device.DeviceType);
    }

    private static string ToFriendlyType(DeviceType type) => type switch
    {
        DeviceType.WindowsPc => "Windows PC",
        DeviceType.LinuxServer => "Linux Server",
        DeviceType.SmartTv => "Smart TV",
        DeviceType.Smartphone => "Smartphone",
        DeviceType.Printer => "Printer",
        DeviceType.Router => "Router",
        DeviceType.IotDevice => "IoT Device",
        DeviceType.Esp32Device => "ESP32 Device",
        DeviceType.GamingConsole => "Gaming Console",
        DeviceType.JellyfinServer => "Jellyfin Server",
        DeviceType.DockerServer => "Docker Server",
        _ => "Unknown Device"
    };

    private static bool Contains(string? value, string searchText)
        => value?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true;

    private static string? Prefer(string? preferred, string? fallback)
        => string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

    private static string? NormalizeVendor(string? vendor)
        => string.Equals(vendor, "Unknown Vendor", StringComparison.OrdinalIgnoreCase) ? null : vendor;

    private static void ApplyHostname(NetworkDevice device, DiscoveredDevice discoveredDevice)
    {
        if (string.IsNullOrWhiteSpace(discoveredDevice.Hostname))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(device.Hostname) || discoveredDevice.HostnameSource >= device.HostnameSource)
        {
            device.Hostname = discoveredDevice.Hostname;
            device.HostnameSource = discoveredDevice.HostnameSource;
        }
    }

    private static bool IsMisleadingServiceAlias(NetworkDevice device)
        => device.HostnameSource == HostnameSource.ReverseDns
            && device.DeviceType != DeviceType.JellyfinServer
            && string.Equals(device.Hostname, "jellyfin", StringComparison.OrdinalIgnoreCase);

    private static bool IsLegacySyntheticHostname(string? currentHostname, string? discoveredHostname)
        => string.Equals(currentHostname, "jellyfin", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(discoveredHostname);

    private static DiscoveredDevice ToDiscoveredDevice(NetworkDevice device)
        => new()
        {
            IpAddress = device.LastIpAddress ?? "0.0.0.0",
            MacAddress = device.MacAddress,
            Hostname = device.Hostname,
            HostnameSource = device.HostnameSource,
            Vendor = device.Vendor,
            DiscoverySource = "Known Device",
            ObservedUtc = device.LastSeenUtc,
            Classification = new DeviceClassification(device.DeviceType, device.ClassificationExplanation, device.DeviceType == DeviceType.UnknownDevice ? 0.2 : 0.9)
        };
}
