namespace Lantern.Application.Abstractions;

public interface INetworkScanPipeline
{
    IAsyncEnumerable<NetworkScanUpdate> ScanAsync(
        NetworkScanOptions options,
        CancellationToken cancellationToken);
}
