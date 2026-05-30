using Lantern.Domain;

namespace Lantern.Application.Abstractions;

public interface INetworkScanner
{
    Task<IReadOnlyList<DeviceObservation>> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
}
