namespace Lantern.Application.Abstractions;

public interface IDhcpLeaseProvider
{
    string Name { get; }
    bool IsEnabled { get; }

    Task<IReadOnlyList<DhcpLeaseRecord>> GetLeasesAsync(CancellationToken cancellationToken = default);
}
