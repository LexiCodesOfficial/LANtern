using Lantern.Domain;

namespace Lantern.Application.Abstractions;

public interface IDeviceRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NetworkDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);
    Task<NetworkDevice?> GetDeviceAsync(Guid id, CancellationToken cancellationToken = default);
    Task<NetworkDevice?> FindByMacAddressAsync(string macAddress, CancellationToken cancellationToken = default);
    Task<NetworkDevice?> FindByIpAddressAsync(string ipAddress, CancellationToken cancellationToken = default);
    Task SaveDeviceAsync(NetworkDevice device, CancellationToken cancellationToken = default);
    Task AddEventAsync(NetworkEvent networkEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NetworkEvent>> GetEventsAsync(Guid? deviceId = null, int take = 100, CancellationToken cancellationToken = default);
    Task ReplaceOpenPortsAsync(Guid deviceId, IEnumerable<int> ports, DateTimeOffset seenUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> GetOpenPortsAsync(Guid deviceId, CancellationToken cancellationToken = default);
    Task PurgeAsync(CancellationToken cancellationToken = default);
}
